using InventoryManager.Models;
using Microsoft.Data.Sqlite;

namespace InventoryManager.Data.Repositories;

public class TransactionRepository
{
    private readonly Database _db;

    public TransactionRepository(Database db) => _db = db;

    public List<Transaction> GetAll(DateTime? from = null, DateTime? to = null)
    {
        var transactions = new List<Transaction>();
        using var conn = _db.GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT t.Id, t.ProductId, p.Name, t.VariantId, t.Type, t.Quantity,
                   t.WarehouseId, w.Name, t.BatchId, t.Reference, t.Notes,
                   t.Date, t.UserId, u.FullName, t.BalanceAfter
            FROM Transactions t
            LEFT JOIN Products p ON p.Id = t.ProductId
            LEFT JOIN Warehouses w ON w.Id = t.WarehouseId
            LEFT JOIN Users u ON u.Id = t.UserId
            WHERE (@from IS NULL OR t.Date >= @from)
              AND (@to IS NULL OR t.Date < @to)
            ORDER BY t.Date DESC, t.Id DESC";
        cmd.Parameters.AddWithValue("@from", (object?)(from?.ToString("o")) ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@to", (object?)(to?.ToString("o")) ?? DBNull.Value);

        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            transactions.Add(new Transaction
            {
                Id = reader.GetInt32(0),
                ProductId = reader.GetInt32(1),
                ProductName = reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
                VariantId = reader.IsDBNull(3) ? null : reader.GetInt32(3),
                Type = (TransactionType)reader.GetInt32(4),
                Quantity = reader.GetInt32(5),
                WarehouseId = reader.GetInt32(6),
                WarehouseName = reader.IsDBNull(7) ? string.Empty : reader.GetString(7),
                BatchId = reader.IsDBNull(8) ? null : reader.GetInt32(8),
                Reference = reader.IsDBNull(9) ? string.Empty : reader.GetString(9),
                Notes = reader.IsDBNull(10) ? string.Empty : reader.GetString(10),
                Date = reader.IsDBNull(11) ? DateTime.MinValue : DateTime.Parse(reader.GetString(11)),
                UserId = reader.IsDBNull(12) ? 0 : reader.GetInt32(12),
                UserName = reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                BalanceAfter = reader.IsDBNull(14) ? 0 : reader.GetInt32(14)
            });
        }

        return transactions;
    }

    public void Insert(SqliteConnection conn, SqliteTransaction tx, Transaction transaction)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO Transactions
                (ProductId, VariantId, Type, Quantity, WarehouseId, BatchId,
                 Reference, Notes, Date, UserId, BalanceAfter)
            VALUES
                (@productId, @variantId, @type, @quantity, @warehouseId, @batchId,
                 @reference, @notes, @date, @userId, @balanceAfter)";
        cmd.Parameters.AddWithValue("@productId", transaction.ProductId);
        cmd.Parameters.AddWithValue("@variantId", (object?)transaction.VariantId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@type", (int)transaction.Type);
        cmd.Parameters.AddWithValue("@quantity", transaction.Quantity);
        cmd.Parameters.AddWithValue("@warehouseId", transaction.WarehouseId);
        cmd.Parameters.AddWithValue("@batchId", (object?)transaction.BatchId ?? DBNull.Value);
        cmd.Parameters.AddWithValue("@reference", transaction.Reference);
        cmd.Parameters.AddWithValue("@notes", transaction.Notes);
        cmd.Parameters.AddWithValue("@date", transaction.Date.ToString("o"));
        cmd.Parameters.AddWithValue("@userId", transaction.UserId);
        cmd.Parameters.AddWithValue("@balanceAfter", transaction.BalanceAfter);
        cmd.ExecuteNonQuery();
    }
}
