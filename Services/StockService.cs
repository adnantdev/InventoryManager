using InventoryManager.Data;
using InventoryManager.Data.Repositories;
using InventoryManager.Models;
using Microsoft.Data.Sqlite;

namespace InventoryManager.Services;

public class StockService
{
    private readonly Database _db;
    private readonly StockRepository _stock;
    private readonly TransactionRepository _tx;
    private readonly BatchRepository _batches;
    private readonly TransferRepository _transfers;
    private readonly AdjustmentRepository _adjustments;

    public StockService(Database db)
    {
        _db = db;
        _stock = new StockRepository(db);
        _tx = new TransactionRepository(db);
        _batches = new BatchRepository(db);
        _transfers = new TransferRepository(db);
        _adjustments = new AdjustmentRepository(db);
    }

    public StockRepository Levels => _stock;
    public BatchRepository Batches => _batches;
    public TransferRepository Transfers => _transfers;
    public AdjustmentRepository Adjustments => _adjustments;

    /// <summary>Stock in / out / sale / return. Optional batch for tracking.</summary>
    public void Move(int productId, int? variantId, int warehouseId,
                     TransactionType type, int qty, int userId,
                     string reference = "", string notes = "",
                     StockBatch? batch = null)
    {
        if (qty <= 0) throw new ArgumentException("Quantity must be greater than zero.");
        if (type == TransactionType.StockOut && batch != null && batch.Quantity < qty)
            throw new InvalidOperationException("Batch does not have enough quantity.");

        int signed = type switch
        {
            TransactionType.StockIn => qty,
            TransactionType.Return => qty,
            TransactionType.StockOut => -qty,
            TransactionType.Sale => -qty,
            _ => throw new InvalidOperationException("Use Adjust() for adjustments.")
        };

        using var conn = _db.GetConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();

        try
        {
            // Optional: attach to or create batch
            int? batchId = null;
            if (batch != null)
            {
                if (batch.Id == 0)
                {
                    if (signed < 0)
                        throw new InvalidOperationException("An existing batch must be selected for an outgoing movement.");
                    batch.WarehouseId = warehouseId;
                    batch.ProductId = productId;
                    batch.VariantId = variantId;
                    batch.Quantity = qty;
                    batchId = _batches.Add(conn, tx, batch);
                }
                else
                {
                    int batchQuantity = GetBatchQuantity(conn, tx, batch.Id, productId, variantId, warehouseId);
                    int updatedBatchQuantity = batchQuantity + signed;
                    if (updatedBatchQuantity < 0)
                        throw new InvalidOperationException($"Batch '{batch.BatchNumber}' does not have enough stock.");
                    UpdateBatchQuantity(conn, tx, batch.Id, updatedBatchQuantity);
                    batchId = batch.Id;
                }
            }

            int balance = _stock.ApplyDelta(conn, tx, productId, variantId, warehouseId, signed);

            _tx.Insert(conn, tx, new Transaction
            {
                ProductId = productId,
                VariantId = variantId,
                Type = type,
                Quantity = signed,
                WarehouseId = warehouseId,
                BatchId = batchId,
                Reference = reference,
                Notes = notes,
                Date = DateTime.Now,
                UserId = userId,
                BalanceAfter = balance
            });

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>Adjust stock to an absolute value.</summary>
    public void Adjust(int productId, int? variantId, int warehouseId,
                       int newQuantity, AdjustmentReason reason, int userId,
                       string notes = "", int? batchId = null)
    {
        if (newQuantity < 0) throw new ArgumentException("New quantity cannot be negative.");

        using var conn = _db.GetConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();

        try
        {
            int old;
            int balance;
            int delta;
            if (batchId.HasValue)
            {
                int batchQuantity = GetBatchQuantity(conn, tx, batchId.Value, productId, variantId, warehouseId);
                old = batchQuantity;
                delta = newQuantity - batchQuantity;
                balance = _stock.ApplyDelta(conn, tx, productId, variantId, warehouseId, delta);
                UpdateBatchQuantity(conn, tx, batchId.Value, newQuantity);
                notes = $"Batch adjustment: {notes}";
            }
            else
            {
                old = _stock.GetTotal(productId, variantId, warehouseId);
                balance = _stock.SetAbsolute(conn, tx, productId, variantId, warehouseId, newQuantity);
                delta = newQuantity - old;
            }

            string refNo = _adjustments.NextReference();

            _adjustments.Insert(conn, tx, new StockAdjustment
            {
                Reference = refNo,
                ProductId = productId,
                VariantId = variantId,
                WarehouseId = warehouseId,
                OldQuantity = old,
                NewQuantity = newQuantity,
                Reason = reason,
                Notes = notes,
                CreatedAt = DateTime.Now,
                UserId = userId
            });

            if (delta != 0)
            {
                _tx.Insert(conn, tx, new Transaction
                {
                    ProductId = productId,
                    VariantId = variantId,
                    Type = TransactionType.Adjustment,
                    Quantity = delta,
                    WarehouseId = warehouseId,
                    Reference = refNo,
                    Notes = $"{reason}: {notes}",
                    Date = DateTime.Now,
                    UserId = userId,
                    BalanceAfter = balance
                });
            }

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    /// <summary>Move stock between two warehouses.</summary>
    public void Transfer(int productId, int? variantId,
                         int fromWarehouseId, int toWarehouseId,
                         int qty, int userId, string notes = "", StockBatch? batch = null)
    {
        if (qty <= 0) throw new ArgumentException("Quantity must be greater than zero.");
        if (fromWarehouseId == toWarehouseId)
            throw new InvalidOperationException("Source and destination must differ.");

        using var conn = _db.GetConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();

        try
        {
            string refNo = _transfers.NextReference();

            _transfers.Insert(conn, tx, new StockTransfer
            {
                Reference = refNo,
                ProductId = productId,
                VariantId = variantId,
                FromWarehouseId = fromWarehouseId,
                ToWarehouseId = toWarehouseId,
                Quantity = qty,
                Status = TransferStatus.Completed,
                Notes = notes,
                CreatedAt = DateTime.Now,
                UserId = userId
            });

            int fromBal = _stock.ApplyDelta(conn, tx, productId, variantId, fromWarehouseId, -qty);
            int toBal = _stock.ApplyDelta(conn, tx, productId, variantId, toWarehouseId, +qty);
            int? fromBatchId = null;
            int? toBatchId = null;
            if (batch != null)
            {
                int batchQuantity = GetBatchQuantity(conn, tx, batch.Id, productId, variantId, fromWarehouseId);
                if (batchQuantity < qty)
                    throw new InvalidOperationException($"Batch '{batch.BatchNumber}' does not have enough stock.");
                UpdateBatchQuantity(conn, tx, batch.Id, batchQuantity - qty);
                fromBatchId = batch.Id;

                var destinationBatch = new StockBatch
                {
                    ProductId = productId,
                    VariantId = variantId,
                    WarehouseId = toWarehouseId,
                    BatchNumber = batch.BatchNumber,
                    LotNumber = batch.LotNumber,
                    Quantity = qty,
                    ManufactureDate = batch.ManufactureDate,
                    ExpiryDate = batch.ExpiryDate,
                    CostPrice = batch.CostPrice,
                    ReceivedAt = DateTime.Now,
                    Supplier = batch.Supplier
                };
                toBatchId = _batches.Add(conn, tx, destinationBatch);
            }

            _tx.Insert(conn, tx, new Transaction
            {
                ProductId = productId, VariantId = variantId,
                Type = TransactionType.Transfer, Quantity = -qty,
                WarehouseId = fromWarehouseId, BatchId = fromBatchId, Reference = refNo,
                Notes = $"Transfer out: {notes}", Date = DateTime.Now,
                UserId = userId, BalanceAfter = fromBal
            });

            _tx.Insert(conn, tx, new Transaction
            {
                ProductId = productId, VariantId = variantId,
                Type = TransactionType.Transfer, Quantity = +qty,
                WarehouseId = toWarehouseId, BatchId = toBatchId, Reference = refNo,
                Notes = $"Transfer in: {notes}", Date = DateTime.Now,
                UserId = userId, BalanceAfter = toBal
            });

            tx.Commit();
        }
        catch
        {
            tx.Rollback();
            throw;
        }
    }

    private static int GetBatchQuantity(SqliteConnection conn, SqliteTransaction tx, int batchId,
                                        int productId, int? variantId, int warehouseId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            SELECT Quantity FROM StockBatches
            WHERE Id=@batchId AND ProductId=@productId AND WarehouseId=@warehouseId
              AND ((VariantId IS NULL AND @variantId IS NULL) OR VariantId=@variantId)";
        cmd.Parameters.AddWithValue("@batchId", batchId);
        cmd.Parameters.AddWithValue("@productId", productId);
        cmd.Parameters.AddWithValue("@variantId", (object?)variantId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@warehouseId", warehouseId);
        var result = cmd.ExecuteScalar();
        if (result == null || result == DBNull.Value)
            throw new InvalidOperationException("The selected batch does not match this product, variant, or location.");
        return Convert.ToInt32(result);
    }

    private static void UpdateBatchQuantity(SqliteConnection conn, SqliteTransaction tx, int batchId, int quantity)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE StockBatches SET Quantity=@quantity WHERE Id=@batchId";
        cmd.Parameters.AddWithValue("@quantity", quantity);
        cmd.Parameters.AddWithValue("@batchId", batchId);
        cmd.ExecuteNonQuery();
    }
}