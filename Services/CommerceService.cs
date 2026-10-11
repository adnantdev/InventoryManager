using InventoryManager.Data;
using InventoryManager.Data.Repositories;
using InventoryManager.Models;
using Microsoft.Data.Sqlite;

namespace InventoryManager.Services;

public class CommerceService
{
    private readonly Database _db;
    private readonly ProductRepository _products;
    private readonly StockRepository _stock;
    private readonly BatchRepository _batches;
    private readonly TransactionRepository _transactions;

    public CommerceService(Database db)
    {
        _db = db;
        _products = new ProductRepository(db);
        _stock = new StockRepository(db);
        _batches = new BatchRepository(db);
        _transactions = new TransactionRepository(db);
    }

    public List<Supplier> GetSuppliers(bool activeOnly = true) => ReadParties<Supplier>("Suppliers", activeOnly);
    public List<Customer> GetCustomers(bool activeOnly = true) => ReadParties<Customer>("Customers", activeOnly);

    public int SaveSupplier(Supplier supplier) => SaveParty("Suppliers", supplier.Id, supplier.Name, supplier.Phone,
        supplier.Email, supplier.Address, supplier.TaxNumber, supplier.IsActive);

    public int SaveCustomer(Customer customer) => SaveParty("Customers", customer.Id, customer.Name, customer.Phone,
        customer.Email, customer.Address, customer.TaxNumber, customer.IsActive);

    public List<TaxRate> GetTaxRates()
    {
        var rates = new List<TaxRate>();
        using var conn = _db.GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Percent, IsActive FROM TaxRates WHERE IsActive=1 ORDER BY Percent, Name";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            rates.Add(new TaxRate
            {
                Id = reader.GetInt32(0),
                Name = reader.GetString(1),
                Percent = (decimal)reader.GetDouble(2),
                IsActive = reader.GetInt32(3) == 1
            });
        return rates;
    }

    public int SaveTaxRate(TaxRate rate)
    {
        if (string.IsNullOrWhiteSpace(rate.Name)) throw new ArgumentException("Tax rate name is required.");
        if (rate.Percent is < 0 or > 100) throw new ArgumentOutOfRangeException(nameof(rate.Percent));
        using var conn = _db.GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = rate.Id == 0
            ? "INSERT INTO TaxRates (Name, Percent, IsActive) VALUES (@name, @percent, 1); SELECT last_insert_rowid();"
            : "UPDATE TaxRates SET Name=@name, Percent=@percent, IsActive=@active WHERE Id=@id; SELECT @id;";
        cmd.Parameters.AddWithValue("@name", rate.Name.Trim());
        cmd.Parameters.AddWithValue("@percent", rate.Percent);
        if (rate.Id != 0)
        {
            cmd.Parameters.AddWithValue("@active", rate.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", rate.Id);
        }
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public List<TaxRate> GetDiscounts()
    {
        var discounts = new List<TaxRate>();
        using var conn = _db.GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id,Name,Percent,IsActive FROM Discounts WHERE IsActive=1 ORDER BY Percent,Name";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            discounts.Add(new TaxRate
            {
                Id = reader.GetInt32(0), Name = reader.GetString(1),
                Percent = (decimal)reader.GetDouble(2), IsActive = reader.GetInt32(3) == 1
            });
        return discounts;
    }

    public int SaveDiscount(TaxRate discount)
    {
        if (string.IsNullOrWhiteSpace(discount.Name)) throw new ArgumentException("Discount name is required.");
        ValidatePercent(discount.Percent, nameof(discount.Percent));
        using var conn = _db.GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = discount.Id == 0
            ? "INSERT INTO Discounts (Name,Percent,IsActive) VALUES (@name,@percent,1); SELECT last_insert_rowid();"
            : "UPDATE Discounts SET Name=@name,Percent=@percent,IsActive=@active WHERE Id=@id; SELECT @id;";
        cmd.Parameters.AddWithValue("@name", discount.Name.Trim());
        cmd.Parameters.AddWithValue("@percent", discount.Percent);
        if (discount.Id != 0)
        {
            cmd.Parameters.AddWithValue("@active", discount.IsActive ? 1 : 0);
            cmd.Parameters.AddWithValue("@id", discount.Id);
        }
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public List<PurchaseOrderSummary> GetPurchaseOrders()
    {
        var list = new List<PurchaseOrderSummary>();
        using var conn = _db.GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT po.Id, po.Number, po.SupplierId, s.Name, po.WarehouseId, w.Name, po.Status,
                   po.OrderDate, COUNT(pol.Id), IFNULL(SUM(pol.Quantity * pol.UnitCost),0), po.Notes
            FROM PurchaseOrders po
            JOIN Suppliers s ON s.Id=po.SupplierId
            JOIN Warehouses w ON w.Id=po.WarehouseId
            LEFT JOIN PurchaseOrderLines pol ON pol.PurchaseOrderId=po.Id
            GROUP BY po.Id ORDER BY po.OrderDate DESC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(new PurchaseOrderSummary
            {
                Id = reader.GetInt32(0), Number = reader.GetString(1), SupplierId = reader.GetInt32(2),
                SupplierName = reader.GetString(3), WarehouseId = reader.GetInt32(4), WarehouseName = reader.GetString(5),
                Status = reader.GetString(6), OrderDate = DateTime.Parse(reader.GetString(7)),
                LineCount = reader.GetInt32(8), Subtotal = (decimal)reader.GetDouble(9),
                Notes = reader.IsDBNull(10) ? "" : reader.GetString(10)
            });
        return list;
    }

    public List<PurchaseInvoiceSummary> GetPurchaseInvoices(int? supplierId = null)
    {
        var list = new List<PurchaseInvoiceSummary>();
        using var conn = _db.GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT i.Id, i.Number, i.SupplierId, s.Name, i.PurchaseOrderId, i.InvoiceDate, i.DueDate,
                   i.Subtotal, i.DiscountPercent, i.TaxPercent, i.TaxAmount, i.Total,
                   i.ReturnedAmount, i.PaidAmount, i.Status, i.Notes
            FROM PurchaseInvoices i JOIN Suppliers s ON s.Id=i.SupplierId
            WHERE (@supplierId IS NULL OR i.SupplierId=@supplierId)
            ORDER BY i.InvoiceDate DESC, i.Id DESC";
        cmd.Parameters.AddWithValue("@supplierId", (object?)supplierId ?? DBNull.Value);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(new PurchaseInvoiceSummary
            {
                Id = reader.GetInt32(0), Number = reader.GetString(1), SupplierId = reader.GetInt32(2),
                SupplierName = reader.GetString(3), PurchaseOrderId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                InvoiceDate = DateTime.Parse(reader.GetString(5)),
                DueDate = reader.IsDBNull(6) ? null : DateTime.Parse(reader.GetString(6)),
                Subtotal = (decimal)reader.GetDouble(7), DiscountPercent = (decimal)reader.GetDouble(8),
                TaxPercent = (decimal)reader.GetDouble(9), TaxAmount = (decimal)reader.GetDouble(10),
                Total = (decimal)reader.GetDouble(11), ReturnedAmount = (decimal)reader.GetDouble(12),
                PaidAmount = (decimal)reader.GetDouble(13), Status = reader.GetString(14),
                Notes = reader.IsDBNull(15) ? "" : reader.GetString(15)
            });
        return list;
    }

    public List<SalesOrderSummary> GetSalesOrders()
    {
        var list = new List<SalesOrderSummary>();
        using var conn = _db.GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT so.Id, so.Number, so.CustomerId, c.Name, so.WarehouseId, w.Name,
                   so.Status, so.OrderDate, COUNT(sol.Id), IFNULL(SUM(sol.Quantity * sol.UnitPrice),0), so.Notes
            FROM SalesOrders so
            JOIN Customers c ON c.Id=so.CustomerId
            JOIN Warehouses w ON w.Id=so.WarehouseId
            LEFT JOIN SalesOrderLines sol ON sol.SalesOrderId=so.Id
            GROUP BY so.Id ORDER BY so.OrderDate DESC";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(new SalesOrderSummary
            {
                Id = reader.GetInt32(0), Number = reader.GetString(1), CustomerId = reader.GetInt32(2),
                CustomerName = reader.GetString(3), WarehouseId = reader.GetInt32(4), WarehouseName = reader.GetString(5),
                Status = reader.GetString(6), OrderDate = DateTime.Parse(reader.GetString(7)),
                LineCount = reader.GetInt32(8), Subtotal = (decimal)reader.GetDouble(9),
                Notes = reader.IsDBNull(10) ? "" : reader.GetString(10)
            });
        return list;
    }

    public List<SalesInvoiceSummary> GetSalesInvoices(int? customerId = null)
    {
        var list = new List<SalesInvoiceSummary>();
        using var conn = _db.GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT i.Id, i.Number, i.CustomerId, c.Name, i.SalesOrderId, i.WarehouseId,
                   i.InvoiceDate, i.Subtotal, i.DiscountPercent, i.TaxPercent, i.TaxAmount,
                   i.Total, i.ReturnedAmount, i.PaidAmount, i.Status, i.Notes
            FROM SalesInvoices i JOIN Customers c ON c.Id=i.CustomerId
            WHERE (@customerId IS NULL OR i.CustomerId=@customerId)
            ORDER BY i.InvoiceDate DESC, i.Id DESC";
        cmd.Parameters.AddWithValue("@customerId", (object?)customerId ?? DBNull.Value);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(new SalesInvoiceSummary
            {
                Id = reader.GetInt32(0), Number = reader.GetString(1), CustomerId = reader.GetInt32(2),
                CustomerName = reader.GetString(3), SalesOrderId = reader.IsDBNull(4) ? null : reader.GetInt32(4),
                WarehouseId = reader.GetInt32(5), InvoiceDate = DateTime.Parse(reader.GetString(6)),
                Subtotal = (decimal)reader.GetDouble(7), DiscountPercent = (decimal)reader.GetDouble(8),
                TaxPercent = (decimal)reader.GetDouble(9), TaxAmount = (decimal)reader.GetDouble(10),
                Total = (decimal)reader.GetDouble(11), ReturnedAmount = (decimal)reader.GetDouble(12),
                PaidAmount = (decimal)reader.GetDouble(13), Status = reader.GetString(14),
                Notes = reader.IsDBNull(15) ? "" : reader.GetString(15)
            });
        return list;
    }

    public int CreatePurchaseOrder(int supplierId, int warehouseId, int productId, int? variantId,
                                   int quantity, decimal unitCost, string notes)
    {
        return CreatePurchaseOrder(supplierId, warehouseId,
            new[] { new CommerceOrderLineInput { ProductId = productId, VariantId = variantId, Quantity = quantity, UnitPrice = unitCost } },
            notes);
    }

    public int CreatePurchaseOrder(int supplierId, int warehouseId, IReadOnlyCollection<CommerceOrderLineInput> lines, string notes)
    {
        if (lines.Count == 0) throw new ArgumentException("Add at least one product to the purchase order.", nameof(lines));
        foreach (var line in lines) ValidateLine(line.Quantity, line.UnitPrice);
        using var conn = _db.GetConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        var number = NextNumber(conn, tx, "PurchaseOrders", "PO");
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO PurchaseOrders (Number,SupplierId,WarehouseId,Status,OrderDate,Notes)
                                VALUES (@number,@supplier,@warehouse,'Open',@date,@notes);
                                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@number", number);
            cmd.Parameters.AddWithValue("@supplier", supplierId);
            cmd.Parameters.AddWithValue("@warehouse", warehouseId);
            cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("o"));
            cmd.Parameters.AddWithValue("@notes", notes);
            var id = Convert.ToInt32(cmd.ExecuteScalar());
            foreach (var line in lines)
            {
                using var insertLine = conn.CreateCommand();
                insertLine.Transaction = tx;
                insertLine.CommandText = @"INSERT INTO PurchaseOrderLines
                    (PurchaseOrderId,ProductId,VariantId,Quantity,UnitCost)
                    VALUES (@order,@product,@variant,@quantity,@cost)";
                insertLine.Parameters.AddWithValue("@order", id);
                insertLine.Parameters.AddWithValue("@product", line.ProductId);
                insertLine.Parameters.AddWithValue("@variant", (object?)line.VariantId ?? DBNull.Value);
                insertLine.Parameters.AddWithValue("@quantity", line.Quantity);
                insertLine.Parameters.AddWithValue("@cost", line.UnitPrice);
                insertLine.ExecuteNonQuery();
            }
            tx.Commit();
            return id;
        }
    }

    public void ReceivePurchaseOrder(int orderId, int userId, string batchNumber, string lotNumber, DateTime? expiryDate)
    {
        using var conn = _db.GetConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            string number;
            int supplierId;
            int warehouseId;
            using (var header = conn.CreateCommand())
            {
                header.Transaction = tx;
                header.CommandText = "SELECT Number,SupplierId,WarehouseId,Status FROM PurchaseOrders WHERE Id=@id";
                header.Parameters.AddWithValue("@id", orderId);
                using var reader = header.ExecuteReader();
                if (!reader.Read()) throw new InvalidOperationException("Purchase order was not found.");
                number = reader.GetString(0);
                supplierId = reader.GetInt32(1);
                warehouseId = reader.GetInt32(2);
                if (reader.GetString(3) != "Open") throw new InvalidOperationException("Only open purchase orders can be received.");
            }

            using var lines = conn.CreateCommand();
            lines.Transaction = tx;
            lines.CommandText = @"SELECT Id,ProductId,VariantId,Quantity,ReceivedQuantity,UnitCost
                                  FROM PurchaseOrderLines WHERE PurchaseOrderId=@id";
            lines.Parameters.AddWithValue("@id", orderId);
            using var lineReader = lines.ExecuteReader();
            var rows = new List<(int Id, int ProductId, int? VariantId, int Qty, int Received, decimal Cost)>();
            while (lineReader.Read())
                rows.Add((lineReader.GetInt32(0), lineReader.GetInt32(1),
                    lineReader.IsDBNull(2) ? null : lineReader.GetInt32(2), lineReader.GetInt32(3),
                    lineReader.GetInt32(4), (decimal)lineReader.GetDouble(5)));
            lineReader.Close();
            if (rows.Count == 0) throw new InvalidOperationException("Purchase order has no lines.");

            foreach (var line in rows)
            {
                var remaining = line.Qty - line.Received;
                if (remaining <= 0) continue;
                var product = _products.GetById(line.ProductId)
                    ?? throw new InvalidOperationException($"Product {line.ProductId} was not found.");
                int? batchId = null;
                if (product.TrackBatches || product.TrackExpiry)
                {
                    var batch = new StockBatch
                    {
                        ProductId = line.ProductId, VariantId = line.VariantId, WarehouseId = warehouseId,
                        BatchNumber = string.IsNullOrWhiteSpace(batchNumber) ? number : batchNumber.Trim(),
                        LotNumber = lotNumber.Trim(), Quantity = remaining, ExpiryDate = expiryDate,
                        CostPrice = line.Cost, Supplier = GetSupplierName(conn, tx, supplierId)
                    };
                    batchId = _batches.Add(conn, tx, batch);
                }
                var balance = _stock.ApplyDelta(conn, tx, line.ProductId, line.VariantId, warehouseId, remaining);
                _transactions.Insert(conn, tx, new Transaction
                {
                    ProductId = line.ProductId, VariantId = line.VariantId, Type = TransactionType.StockIn,
                    Quantity = remaining, WarehouseId = warehouseId, BatchId = batchId,
                    Reference = $"GRN-{number}", Notes = "Goods received against purchase order",
                    Date = DateTime.Now, UserId = userId, BalanceAfter = balance
                });
                using var updateLine = conn.CreateCommand();
                updateLine.Transaction = tx;
                updateLine.CommandText = "UPDATE PurchaseOrderLines SET ReceivedQuantity=Quantity,BatchId=@batch WHERE Id=@id";
                updateLine.Parameters.AddWithValue("@batch", (object?)batchId ?? DBNull.Value);
                updateLine.Parameters.AddWithValue("@id", line.Id);
                updateLine.ExecuteNonQuery();
            }

            using var update = conn.CreateCommand();
            update.Transaction = tx;
            update.CommandText = "UPDATE PurchaseOrders SET Status='Received' WHERE Id=@id";
            update.Parameters.AddWithValue("@id", orderId);
            update.ExecuteNonQuery();
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    public int CreatePurchaseInvoiceFromOrder(int orderId, decimal discountPercent, decimal taxPercent, DateTime? dueDate)
    {
        ValidatePercent(discountPercent, nameof(discountPercent));
        ValidatePercent(taxPercent, nameof(taxPercent));
        using var conn = _db.GetConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            int supplierId;
            using (var order = conn.CreateCommand())
            {
                order.Transaction = tx;
                order.CommandText = "SELECT SupplierId,Status FROM PurchaseOrders WHERE Id=@id";
                order.Parameters.AddWithValue("@id", orderId);
                using var reader = order.ExecuteReader();
                if (!reader.Read()) throw new InvalidOperationException("Purchase order was not found.");
                supplierId = reader.GetInt32(0);
                if (reader.GetString(1) != "Received") throw new InvalidOperationException("Receive goods before invoicing this order.");
            }

            var lines = ReadPurchaseOrderLines(conn, tx, orderId);
            if (lines.Count == 0) throw new InvalidOperationException("No received goods are available to invoice.");
            var subtotal = lines.Sum(line => line.Quantity * line.UnitCost);
            var taxable = RoundMoney(subtotal * (1 - discountPercent / 100));
            var tax = RoundMoney(taxable * taxPercent / 100);
            var total = taxable + tax;
            var invoiceNumber = NextNumber(conn, tx, "PurchaseInvoices", "PINV");
            int invoiceId;
            using (var invoice = conn.CreateCommand())
            {
                invoice.Transaction = tx;
                invoice.CommandText = @"INSERT INTO PurchaseInvoices
                    (Number,SupplierId,PurchaseOrderId,InvoiceDate,DueDate,Subtotal,DiscountPercent,TaxPercent,TaxAmount,Total,Status)
                    VALUES (@number,@supplier,@order,@date,@due,@subtotal,@discount,@taxPercent,@tax,@total,'Unpaid');
                    SELECT last_insert_rowid();";
                invoice.Parameters.AddWithValue("@number", invoiceNumber);
                invoice.Parameters.AddWithValue("@supplier", supplierId);
                invoice.Parameters.AddWithValue("@order", orderId);
                invoice.Parameters.AddWithValue("@date", DateTime.Now.ToString("o"));
                invoice.Parameters.AddWithValue("@due", (object?)dueDate?.ToString("o") ?? DBNull.Value);
                invoice.Parameters.AddWithValue("@subtotal", subtotal);
                invoice.Parameters.AddWithValue("@discount", discountPercent);
                invoice.Parameters.AddWithValue("@taxPercent", taxPercent);
                invoice.Parameters.AddWithValue("@tax", tax);
                invoice.Parameters.AddWithValue("@total", total);
                invoiceId = Convert.ToInt32(invoice.ExecuteScalar());
            }

            foreach (var line in lines)
            {
                using var insert = conn.CreateCommand();
                insert.Transaction = tx;
                insert.CommandText = @"INSERT INTO PurchaseInvoiceLines
                    (PurchaseInvoiceId,ProductId,VariantId,Quantity,UnitCost,BatchId)
                    VALUES (@invoice,@product,@variant,@quantity,@cost,@batch)";
                insert.Parameters.AddWithValue("@invoice", invoiceId);
                insert.Parameters.AddWithValue("@product", line.ProductId);
                insert.Parameters.AddWithValue("@variant", (object?)line.VariantId ?? DBNull.Value);
                insert.Parameters.AddWithValue("@quantity", line.Quantity);
                insert.Parameters.AddWithValue("@cost", line.UnitCost);
                insert.Parameters.AddWithValue("@batch", (object?)line.BatchId ?? DBNull.Value);
                insert.ExecuteNonQuery();
            }
            using (var markInvoiced = conn.CreateCommand())
            {
                markInvoiced.Transaction = tx;
                markInvoiced.CommandText = "UPDATE PurchaseOrders SET Status='Invoiced' WHERE Id=@id";
                markInvoiced.Parameters.AddWithValue("@id", orderId);
                markInvoiced.ExecuteNonQuery();
            }
            tx.Commit();
            return invoiceId;
        }
        catch { tx.Rollback(); throw; }
    }

    public void RecordSupplierPayment(int invoiceId, decimal amount, string method, string reference, string notes)
    {
        RecordPayment("PurchaseInvoices", "SupplierPayments", "SupplierId", invoiceId, amount, method, reference, notes);
    }

    public decimal ReturnPurchaseInvoice(int invoiceId, int lineId, int quantity, int userId, string notes)
    {
        if (quantity <= 0) throw new ArgumentException("Return quantity must be greater than zero.");
        using var conn = _db.GetConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            int supplierId;
            decimal discountPercent;
            decimal taxPercent;
            using (var invoice = conn.CreateCommand())
            {
                invoice.Transaction = tx;
                invoice.CommandText = "SELECT SupplierId,DiscountPercent,TaxPercent FROM PurchaseInvoices WHERE Id=@id";
                invoice.Parameters.AddWithValue("@id", invoiceId);
                using var reader = invoice.ExecuteReader();
                if (!reader.Read()) throw new InvalidOperationException("Purchase invoice was not found.");
                supplierId = reader.GetInt32(0);
                discountPercent = (decimal)reader.GetDouble(1);
                taxPercent = (decimal)reader.GetDouble(2);
            }
            int productId;
            int? variantId;
            int warehouseId;
            int invoiced;
            int returned;
            int? batchId;
            decimal unitCost;
            using (var line = conn.CreateCommand())
            {
                line.Transaction = tx;
                line.CommandText = @"SELECT l.ProductId,l.VariantId,l.Quantity,l.ReturnedQuantity,l.BatchId,l.UnitCost,i.PurchaseOrderId,po.WarehouseId
                    FROM PurchaseInvoiceLines l JOIN PurchaseInvoices i ON i.Id=l.PurchaseInvoiceId
                    LEFT JOIN PurchaseOrders po ON po.Id=i.PurchaseOrderId
                    WHERE l.Id=@line AND l.PurchaseInvoiceId=@invoice";
                line.Parameters.AddWithValue("@line", lineId);
                line.Parameters.AddWithValue("@invoice", invoiceId);
                using var reader = line.ExecuteReader();
                if (!reader.Read()) throw new InvalidOperationException("Purchase invoice line was not found.");
                productId = reader.GetInt32(0);
                variantId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                invoiced = reader.GetInt32(2);
                returned = reader.GetInt32(3);
                batchId = reader.IsDBNull(4) ? null : reader.GetInt32(4);
                unitCost = (decimal)reader.GetDouble(5);
                if (reader.IsDBNull(7)) throw new InvalidOperationException("This invoice is not linked to a warehouse.");
                warehouseId = reader.GetInt32(7);
            }
            if (quantity > invoiced - returned) throw new InvalidOperationException("Return quantity exceeds the unreturned quantity.");
            var balance = _stock.ApplyDelta(conn, tx, productId, variantId, warehouseId, -quantity);
            if (batchId.HasValue) UpdateBatchQuantity(conn, tx, batchId.Value, -quantity);

            var reference = NextNumber(conn, tx, "PurchaseReturns", "PRET");
            var amount = RoundMoney(unitCost * quantity * (1 - discountPercent / 100) * (1 + taxPercent / 100));
            int returnId;
            using (var insert = conn.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = @"INSERT INTO PurchaseReturns (Number,SupplierId,PurchaseInvoiceId,ReturnDate,Total,Notes)
                    VALUES (@number,@supplier,@invoice,@date,@total,@notes); SELECT last_insert_rowid();";
                insert.Parameters.AddWithValue("@number", reference);
                insert.Parameters.AddWithValue("@supplier", supplierId);
                insert.Parameters.AddWithValue("@invoice", invoiceId);
                insert.Parameters.AddWithValue("@date", DateTime.Now.ToString("o"));
                insert.Parameters.AddWithValue("@total", amount);
                insert.Parameters.AddWithValue("@notes", notes);
                returnId = Convert.ToInt32(insert.ExecuteScalar());
            }
            using (var line = conn.CreateCommand())
            {
                line.Transaction = tx;
                line.CommandText = @"INSERT INTO PurchaseReturnLines
                    (PurchaseReturnId,PurchaseInvoiceLineId,ProductId,VariantId,Quantity,UnitCost,BatchId)
                    VALUES (@return,@line,@product,@variant,@quantity,@cost,@batch)";
                line.Parameters.AddWithValue("@return", returnId);
                line.Parameters.AddWithValue("@line", lineId);
                line.Parameters.AddWithValue("@product", productId);
                line.Parameters.AddWithValue("@variant", (object?)variantId ?? DBNull.Value);
                line.Parameters.AddWithValue("@quantity", quantity);
                line.Parameters.AddWithValue("@cost", unitCost);
                line.Parameters.AddWithValue("@batch", (object?)batchId ?? DBNull.Value);
                line.ExecuteNonQuery();
            }
            using (var updateLine = conn.CreateCommand())
            {
                updateLine.Transaction = tx;
                updateLine.CommandText = "UPDATE PurchaseInvoiceLines SET ReturnedQuantity=ReturnedQuantity+@quantity WHERE Id=@id";
                updateLine.Parameters.AddWithValue("@quantity", quantity);
                updateLine.Parameters.AddWithValue("@id", lineId);
                updateLine.ExecuteNonQuery();
            }
            using (var updateInvoice = conn.CreateCommand())
            {
                updateInvoice.Transaction = tx;
                updateInvoice.CommandText = @"UPDATE PurchaseInvoices SET ReturnedAmount=ReturnedAmount+@amount,
                    Status=CASE WHEN PaidAmount>=Total-ReturnedAmount-@amount THEN 'Paid' ELSE 'PartiallyPaid' END WHERE Id=@id";
                updateInvoice.Parameters.AddWithValue("@amount", amount);
                updateInvoice.Parameters.AddWithValue("@id", invoiceId);
                updateInvoice.ExecuteNonQuery();
            }
            _transactions.Insert(conn, tx, new Transaction
            {
                ProductId = productId, VariantId = variantId, Type = TransactionType.StockOut,
                Quantity = -quantity, WarehouseId = warehouseId, BatchId = batchId, Reference = reference,
                Notes = $"Purchase return: {notes}", Date = DateTime.Now, UserId = userId, BalanceAfter = balance
            });
            tx.Commit();
            return amount;
        }
        catch { tx.Rollback(); throw; }
    }

    public int CreateSalesOrder(int customerId, int warehouseId, int productId, int? variantId,
                                int quantity, decimal unitPrice, string notes)
    {
        return CreateSalesOrder(customerId, warehouseId,
            new[] { new CommerceOrderLineInput { ProductId = productId, VariantId = variantId, Quantity = quantity, UnitPrice = unitPrice } },
            notes);
    }

    public int CreateSalesOrder(int customerId, int warehouseId, IReadOnlyCollection<CommerceOrderLineInput> lines, string notes)
    {
        if (lines.Count == 0) throw new ArgumentException("Add at least one product to the sales order.", nameof(lines));
        foreach (var line in lines) ValidateLine(line.Quantity, line.UnitPrice);
        using var conn = _db.GetConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        var number = NextNumber(conn, tx, "SalesOrders", "SO");
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"INSERT INTO SalesOrders (Number,CustomerId,WarehouseId,Status,OrderDate,Notes)
                                VALUES (@number,@customer,@warehouse,'Open',@date,@notes);
                                SELECT last_insert_rowid();";
            cmd.Parameters.AddWithValue("@number", number);
            cmd.Parameters.AddWithValue("@customer", customerId);
            cmd.Parameters.AddWithValue("@warehouse", warehouseId);
            cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("o"));
            cmd.Parameters.AddWithValue("@notes", notes);
            var id = Convert.ToInt32(cmd.ExecuteScalar());
            foreach (var line in lines)
            {
                using var insertLine = conn.CreateCommand();
                insertLine.Transaction = tx;
                insertLine.CommandText = @"INSERT INTO SalesOrderLines (SalesOrderId,ProductId,VariantId,Quantity,UnitPrice)
                                           VALUES (@order,@product,@variant,@quantity,@price)";
                insertLine.Parameters.AddWithValue("@order", id);
                insertLine.Parameters.AddWithValue("@product", line.ProductId);
                insertLine.Parameters.AddWithValue("@variant", (object?)line.VariantId ?? DBNull.Value);
                insertLine.Parameters.AddWithValue("@quantity", line.Quantity);
                insertLine.Parameters.AddWithValue("@price", line.UnitPrice);
                insertLine.ExecuteNonQuery();
            }
            tx.Commit();
            return id;
        }
    }

    public int CreatePosSale(int customerId, int warehouseId, int productId, int? variantId,
                             int quantity, decimal unitPrice, decimal discountPercent, decimal taxPercent,
                             int userId, string notes)
    {
        var orderId = CreateSalesOrder(customerId, warehouseId, productId, variantId, quantity, unitPrice, notes);
        return InvoiceSalesOrder(orderId, discountPercent, taxPercent, userId);
    }

    public int CreatePosSale(int customerId, int warehouseId, IReadOnlyCollection<CommerceOrderLineInput> lines,
                             decimal discountPercent, decimal taxPercent, int userId, string notes)
    {
        var orderId = CreateSalesOrder(customerId, warehouseId, lines, notes);
        return InvoiceSalesOrder(orderId, discountPercent, taxPercent, userId);
    }

    public int InvoiceSalesOrder(int orderId, decimal discountPercent, decimal taxPercent, int userId)
    {
        ValidatePercent(discountPercent, nameof(discountPercent));
        ValidatePercent(taxPercent, nameof(taxPercent));
        using var conn = _db.GetConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            int customerId;
            int warehouseId;
            using (var order = conn.CreateCommand())
            {
                order.Transaction = tx;
                order.CommandText = "SELECT CustomerId,WarehouseId,Status FROM SalesOrders WHERE Id=@id";
                order.Parameters.AddWithValue("@id", orderId);
                using var reader = order.ExecuteReader();
                if (!reader.Read()) throw new InvalidOperationException("Sales order was not found.");
                customerId = reader.GetInt32(0);
                warehouseId = reader.GetInt32(1);
                if (reader.GetString(2) != "Open") throw new InvalidOperationException("Sales order is already invoiced or cancelled.");
            }
            var lines = ReadSalesOrderLines(conn, tx, orderId);
            if (lines.Count == 0) throw new InvalidOperationException("Sales order has no lines.");
            var subtotal = lines.Sum(line => line.Quantity * line.UnitPrice);
            var taxable = RoundMoney(subtotal * (1 - discountPercent / 100));
            var tax = RoundMoney(taxable * taxPercent / 100);
            var total = taxable + tax;
            var number = NextNumber(conn, tx, "SalesInvoices", "SINV");
            int invoiceId;
            using (var invoice = conn.CreateCommand())
            {
                invoice.Transaction = tx;
                invoice.CommandText = @"INSERT INTO SalesInvoices
                    (Number,SalesOrderId,CustomerId,WarehouseId,InvoiceDate,Subtotal,DiscountPercent,TaxPercent,TaxAmount,Total,Status)
                    VALUES (@number,@order,@customer,@warehouse,@date,@subtotal,@discount,@taxPercent,@tax,@total,'Unpaid');
                    SELECT last_insert_rowid();";
                invoice.Parameters.AddWithValue("@number", number);
                invoice.Parameters.AddWithValue("@order", orderId);
                invoice.Parameters.AddWithValue("@customer", customerId);
                invoice.Parameters.AddWithValue("@warehouse", warehouseId);
                invoice.Parameters.AddWithValue("@date", DateTime.Now.ToString("o"));
                invoice.Parameters.AddWithValue("@subtotal", subtotal);
                invoice.Parameters.AddWithValue("@discount", discountPercent);
                invoice.Parameters.AddWithValue("@taxPercent", taxPercent);
                invoice.Parameters.AddWithValue("@tax", tax);
                invoice.Parameters.AddWithValue("@total", total);
                invoiceId = Convert.ToInt32(invoice.ExecuteScalar());
            }

            foreach (var line in lines)
            {
                var product = _products.GetById(line.ProductId)
                    ?? throw new InvalidOperationException($"Product {line.ProductId} was not found.");
                int invoiceLineId;
                using (var insertLine = conn.CreateCommand())
                {
                    insertLine.Transaction = tx;
                    insertLine.CommandText = @"INSERT INTO SalesInvoiceLines
                        (SalesInvoiceId,ProductId,VariantId,Quantity,UnitPrice,UnitCost)
                        VALUES (@invoice,@product,@variant,@quantity,@price,@cost);
                        SELECT last_insert_rowid();";
                    insertLine.Parameters.AddWithValue("@invoice", invoiceId);
                    insertLine.Parameters.AddWithValue("@product", line.ProductId);
                    insertLine.Parameters.AddWithValue("@variant", (object?)line.VariantId ?? DBNull.Value);
                    insertLine.Parameters.AddWithValue("@quantity", line.Quantity);
                    insertLine.Parameters.AddWithValue("@price", line.UnitPrice);
                    insertLine.Parameters.AddWithValue("@cost", product.CostPrice);
                    invoiceLineId = Convert.ToInt32(insertLine.ExecuteScalar());
                }

                if (product.TrackBatches || product.TrackExpiry)
                {
                    ConsumeBatches(conn, tx, line.ProductId, line.VariantId, warehouseId, line.Quantity,
                        invoiceLineId, userId, number);
                }
                else
                {
                    var balance = _stock.ApplyDelta(conn, tx, line.ProductId, line.VariantId, warehouseId, -line.Quantity);
                    _transactions.Insert(conn, tx, new Transaction
                    {
                        ProductId = line.ProductId, VariantId = line.VariantId, Type = TransactionType.Sale,
                        Quantity = -line.Quantity, WarehouseId = warehouseId, Reference = number,
                        Notes = "Sales invoice", Date = DateTime.Now, UserId = userId, BalanceAfter = balance
                    });
                }
            }
            using (var update = conn.CreateCommand())
            {
                update.Transaction = tx;
                update.CommandText = "UPDATE SalesOrders SET Status='Invoiced' WHERE Id=@id";
                update.Parameters.AddWithValue("@id", orderId);
                update.ExecuteNonQuery();
            }
            tx.Commit();
            return invoiceId;
        }
        catch { tx.Rollback(); throw; }
    }

    public void RecordCustomerPayment(int invoiceId, decimal amount, string method, string reference, string notes)
    {
        RecordPayment("SalesInvoices", "CustomerPayments", "CustomerId", invoiceId, amount, method, reference, notes);
    }

    public decimal ReturnSalesInvoice(int invoiceId, int lineId, int quantity, int userId, string notes)
    {
        if (quantity <= 0) throw new ArgumentException("Return quantity must be greater than zero.");
        using var conn = _db.GetConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            int customerId;
            int warehouseId;
            decimal discountPercent;
            decimal taxPercent;
            using (var invoice = conn.CreateCommand())
            {
                invoice.Transaction = tx;
                invoice.CommandText = "SELECT CustomerId,WarehouseId,DiscountPercent,TaxPercent FROM SalesInvoices WHERE Id=@id";
                invoice.Parameters.AddWithValue("@id", invoiceId);
                using var reader = invoice.ExecuteReader();
                if (!reader.Read()) throw new InvalidOperationException("Sales invoice was not found.");
                customerId = reader.GetInt32(0);
                warehouseId = reader.GetInt32(1);
                discountPercent = (decimal)reader.GetDouble(2);
                taxPercent = (decimal)reader.GetDouble(3);
            }
            int productId;
            int? variantId;
            int invoiced;
            int returned;
            decimal unitPrice;
            using (var line = conn.CreateCommand())
            {
                line.Transaction = tx;
                line.CommandText = @"SELECT ProductId,VariantId,Quantity,ReturnedQuantity,UnitPrice
                    FROM SalesInvoiceLines WHERE Id=@line AND SalesInvoiceId=@invoice";
                line.Parameters.AddWithValue("@line", lineId);
                line.Parameters.AddWithValue("@invoice", invoiceId);
                using var reader = line.ExecuteReader();
                if (!reader.Read()) throw new InvalidOperationException("Sales invoice line was not found.");
                productId = reader.GetInt32(0);
                variantId = reader.IsDBNull(1) ? null : reader.GetInt32(1);
                invoiced = reader.GetInt32(2);
                returned = reader.GetInt32(3);
                unitPrice = (decimal)reader.GetDouble(4);
            }
            if (quantity > invoiced - returned) throw new InvalidOperationException("Return quantity exceeds the unreturned quantity.");
            var product = _products.GetById(productId)
                ?? throw new InvalidOperationException($"Product {productId} was not found.");
            var reference = NextNumber(conn, tx, "SalesReturns", "SRET");
            var refund = RoundMoney(unitPrice * quantity * (1 - discountPercent / 100) * (1 + taxPercent / 100));
            int returnId;
            using (var insert = conn.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = @"INSERT INTO SalesReturns (Number,CustomerId,SalesInvoiceId,ReturnDate,Total,Notes)
                    VALUES (@number,@customer,@invoice,@date,@total,@notes); SELECT last_insert_rowid();";
                insert.Parameters.AddWithValue("@number", reference);
                insert.Parameters.AddWithValue("@customer", customerId);
                insert.Parameters.AddWithValue("@invoice", invoiceId);
                insert.Parameters.AddWithValue("@date", DateTime.Now.ToString("o"));
                insert.Parameters.AddWithValue("@total", refund);
                insert.Parameters.AddWithValue("@notes", notes);
                returnId = Convert.ToInt32(insert.ExecuteScalar());
            }

            var allocations = product.TrackBatches || product.TrackExpiry
                ? ConsumeReturnBatchAllocations(conn, tx, lineId, quantity)
                : new List<(int? BatchId, int Quantity)> { (null, quantity) };
            var balance = 0;
            foreach (var allocation in allocations)
            {
                balance = _stock.ApplyDelta(conn, tx, productId, variantId, warehouseId, allocation.Quantity);
                if (allocation.BatchId.HasValue) UpdateBatchQuantity(conn, tx, allocation.BatchId.Value, allocation.Quantity);
                using var insertLine = conn.CreateCommand();
                insertLine.Transaction = tx;
                insertLine.CommandText = @"INSERT INTO SalesReturnLines
                    (SalesReturnId,SalesInvoiceLineId,ProductId,VariantId,Quantity,UnitPrice)
                    VALUES (@return,@line,@product,@variant,@quantity,@price)";
                insertLine.Parameters.AddWithValue("@return", returnId);
                insertLine.Parameters.AddWithValue("@line", lineId);
                insertLine.Parameters.AddWithValue("@product", productId);
                insertLine.Parameters.AddWithValue("@variant", (object?)variantId ?? DBNull.Value);
                insertLine.Parameters.AddWithValue("@quantity", allocation.Quantity);
                insertLine.Parameters.AddWithValue("@price", unitPrice);
                insertLine.ExecuteNonQuery();
                _transactions.Insert(conn, tx, new Transaction
                {
                    ProductId = productId, VariantId = variantId, Type = TransactionType.Return,
                    Quantity = allocation.Quantity, WarehouseId = warehouseId, BatchId = allocation.BatchId,
                    Reference = reference, Notes = notes, Date = DateTime.Now, UserId = userId, BalanceAfter = balance
                });
            }
            using (var updateLine = conn.CreateCommand())
            {
                updateLine.Transaction = tx;
                updateLine.CommandText = "UPDATE SalesInvoiceLines SET ReturnedQuantity=ReturnedQuantity+@quantity WHERE Id=@id";
                updateLine.Parameters.AddWithValue("@quantity", quantity);
                updateLine.Parameters.AddWithValue("@id", lineId);
                updateLine.ExecuteNonQuery();
            }
            using (var updateInvoice = conn.CreateCommand())
            {
                updateInvoice.Transaction = tx;
                updateInvoice.CommandText = @"UPDATE SalesInvoices SET ReturnedAmount=ReturnedAmount+@refund,
                    Status=CASE WHEN PaidAmount>=Total-ReturnedAmount-@refund THEN 'Paid' ELSE 'PartiallyPaid' END WHERE Id=@id";
                updateInvoice.Parameters.AddWithValue("@refund", refund);
                updateInvoice.Parameters.AddWithValue("@id", invoiceId);
                updateInvoice.ExecuteNonQuery();
            }
            tx.Commit();
            return refund;
        }
        catch { tx.Rollback(); throw; }
    }

    public List<CommerceLine> GetPurchaseInvoiceLines(int invoiceId)
        => GetInvoiceLines("PurchaseInvoiceLines", "PurchaseInvoiceId", invoiceId, "UnitCost");

    public List<CommerceLine> GetSalesInvoiceLines(int invoiceId)
        => GetInvoiceLines("SalesInvoiceLines", "SalesInvoiceId", invoiceId, "UnitPrice");

    public List<PartyHistoryEntry> GetSupplierHistory(int supplierId)
        => GetPartyHistory(true, supplierId);

    public List<PartyHistoryEntry> GetCustomerHistory(int customerId)
        => GetPartyHistory(false, customerId);

    public CommerceMetrics GetSalesMetrics()
        => GetMetrics("SalesInvoices", "Total-ReturnedAmount");

    public CommerceMetrics GetPurchaseMetrics()
        => GetMetrics("PurchaseInvoices", "Total-ReturnedAmount");

    public (decimal revenue, decimal cost, decimal profit) GetProfitLoss(DateTime from, DateTime to)
    {
        using var conn = _db.GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT IFNULL(SUM((Subtotal*(1-DiscountPercent/100)) -
                (ReturnedAmount/(1+TaxPercent/100))),0)
            FROM SalesInvoices WHERE InvoiceDate>=@from AND InvoiceDate<@to";
        cmd.Parameters.AddWithValue("@from", from.ToString("o"));
        cmd.Parameters.AddWithValue("@to", to.ToString("o"));
        var revenue = Convert.ToDecimal(cmd.ExecuteScalar());
        cmd.CommandText = @"
            SELECT IFNULL(SUM((l.Quantity-l.ReturnedQuantity)*l.UnitCost),0)
            FROM SalesInvoiceLines l JOIN SalesInvoices i ON i.Id=l.SalesInvoiceId
            WHERE i.InvoiceDate>=@from AND i.InvoiceDate<@to";
        var cost = Convert.ToDecimal(cmd.ExecuteScalar());
        return (revenue, cost, revenue - cost);
    }

    public decimal GetStockValuation() =>
        _products.GetAll().Sum(product => product.CostPrice * product.TotalQuantity);

    public List<CommerceReportRow> GetSalesReport(DateTime from, DateTime to)
    {
        var rows = new List<CommerceReportRow>();
        using var conn = _db.GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT i.InvoiceDate,i.Number,c.Name,p.Name,(l.Quantity-l.ReturnedQuantity),
                   l.UnitPrice,l.UnitCost,i.DiscountPercent,i.TaxPercent
            FROM SalesInvoices i JOIN Customers c ON c.Id=i.CustomerId
            JOIN SalesInvoiceLines l ON l.SalesInvoiceId=i.Id
            JOIN Products p ON p.Id=l.ProductId
            WHERE i.InvoiceDate>=@from AND i.InvoiceDate<@to
            ORDER BY i.InvoiceDate DESC";
        cmd.Parameters.AddWithValue("@from", from.ToString("o"));
        cmd.Parameters.AddWithValue("@to", to.ToString("o"));
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var quantity = reader.GetInt32(4);
            var discount = (decimal)reader.GetDouble(7);
            var taxRate = (decimal)reader.GetDouble(8);
            var net = RoundMoney((decimal)reader.GetDouble(5) * quantity * (1 - discount / 100));
            rows.Add(new CommerceReportRow
            {
                Date = DateTime.Parse(reader.GetString(0)), Reference = reader.GetString(1),
                Party = reader.GetString(2), Product = reader.GetString(3), Quantity = quantity,
                Revenue = net, Cost = RoundMoney((decimal)reader.GetDouble(6) * quantity),
                Tax = RoundMoney(net * taxRate / 100)
            });
        }
        return rows;
    }

    public List<CommerceReportRow> GetPurchaseReport(DateTime from, DateTime to)
    {
        var rows = new List<CommerceReportRow>();
        using var conn = _db.GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT i.InvoiceDate,i.Number,s.Name,p.Name,(l.Quantity-l.ReturnedQuantity),
                   l.UnitCost,i.DiscountPercent,i.TaxPercent
            FROM PurchaseInvoices i JOIN Suppliers s ON s.Id=i.SupplierId
            JOIN PurchaseInvoiceLines l ON l.PurchaseInvoiceId=i.Id
            JOIN Products p ON p.Id=l.ProductId
            WHERE i.InvoiceDate>=@from AND i.InvoiceDate<@to
            ORDER BY i.InvoiceDate DESC";
        cmd.Parameters.AddWithValue("@from", from.ToString("o"));
        cmd.Parameters.AddWithValue("@to", to.ToString("o"));
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var qty = reader.GetInt32(4);
            var net = RoundMoney((decimal)reader.GetDouble(5) * qty *
                                 (1 - (decimal)reader.GetDouble(6) / 100));
            rows.Add(new CommerceReportRow
            {
                Date = DateTime.Parse(reader.GetString(0)), Reference = reader.GetString(1),
                Party = reader.GetString(2), Product = reader.GetString(3), Quantity = qty,
                Revenue = net, Tax = RoundMoney(net * (decimal)reader.GetDouble(7) / 100)
            });
        }
        return rows;
    }

    public List<CommerceReportRow> GetProductMovementReport(int days)
        => GetProductMovementReport(DateTime.Today.AddDays(-days), DateTime.Today.AddDays(1));

    public List<CommerceReportRow> GetProductMovementReport(DateTime from, DateTime to)
    {
        var rows = new List<CommerceReportRow>();
        using var conn = _db.GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT p.Name,IFNULL(SUM(l.Quantity-l.ReturnedQuantity),0),
                   IFNULL(SUM((l.Quantity-l.ReturnedQuantity)*l.UnitPrice),0)
            FROM Products p
            LEFT JOIN SalesInvoiceLines l ON l.ProductId=p.Id
                AND l.SalesInvoiceId IN (
                    SELECT Id FROM SalesInvoices WHERE InvoiceDate>=@from AND InvoiceDate<@to)
            GROUP BY p.Id ORDER BY 2 DESC,p.Name";
        cmd.Parameters.AddWithValue("@from", from.ToString("o"));
        cmd.Parameters.AddWithValue("@to", to.ToString("o"));
        using var reader = cmd.ExecuteReader();
        var position = 0;
        while (reader.Read())
        {
            position++;
            rows.Add(new CommerceReportRow
            {
                Product = reader.GetString(0), Quantity = reader.GetInt32(1),
                Revenue = (decimal)reader.GetDouble(2), Reference = position.ToString()
            });
        }
        return rows;
    }

    private List<T> ReadParties<T>(string table, bool activeOnly) where T : class, new()
    {
        var list = new List<T>();
        using var conn = _db.GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT Id,Name,Phone,Email,Address,TaxNumber,IsActive FROM {table}" +
                          (activeOnly ? " WHERE IsActive=1" : "") + " ORDER BY Name";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            object party = typeof(T) == typeof(Supplier) ? new Supplier
            {
                Id = reader.GetInt32(0), Name = reader.GetString(1),
                Phone = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Email = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Address = reader.IsDBNull(4) ? "" : reader.GetString(4),
                TaxNumber = reader.IsDBNull(5) ? "" : reader.GetString(5),
                IsActive = reader.GetInt32(6) == 1
            } : new Customer
            {
                Id = reader.GetInt32(0), Name = reader.GetString(1),
                Phone = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Email = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Address = reader.IsDBNull(4) ? "" : reader.GetString(4),
                TaxNumber = reader.IsDBNull(5) ? "" : reader.GetString(5),
                IsActive = reader.GetInt32(6) == 1
            };
            list.Add((T)party);
        }
        return list;
    }

    private int SaveParty(string table, int id, string name, string phone, string email, string address, string taxNumber, bool active)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name is required.");
        using var conn = _db.GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = id == 0
            ? $"INSERT INTO {table} (Name,Phone,Email,Address,TaxNumber,IsActive,CreatedAt) VALUES (@name,@phone,@email,@address,@tax,@active,@date); SELECT last_insert_rowid();"
            : $"UPDATE {table} SET Name=@name,Phone=@phone,Email=@email,Address=@address,TaxNumber=@tax,IsActive=@active WHERE Id=@id; SELECT @id;";
        cmd.Parameters.AddWithValue("@name", name.Trim());
        cmd.Parameters.AddWithValue("@phone", phone.Trim());
        cmd.Parameters.AddWithValue("@email", email.Trim());
        cmd.Parameters.AddWithValue("@address", address.Trim());
        cmd.Parameters.AddWithValue("@tax", taxNumber.Trim());
        cmd.Parameters.AddWithValue("@active", active ? 1 : 0);
        if (id == 0) cmd.Parameters.AddWithValue("@date", DateTime.Now.ToString("o"));
        else cmd.Parameters.AddWithValue("@id", id);
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    private static void ValidateLine(int quantity, decimal unitPrice)
    {
        if (quantity <= 0) throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        if (unitPrice < 0) throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");
    }

    private static void ValidatePercent(decimal percent, string name)
    {
        if (percent is < 0 or > 100) throw new ArgumentOutOfRangeException(name, "Percentage must be between 0 and 100.");
    }

    private static decimal RoundMoney(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    private static string NextNumber(SqliteConnection conn, SqliteTransaction tx, string table, string prefix)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT IFNULL(MAX(Id),0)+1 FROM {table}";
        var next = Convert.ToInt32(cmd.ExecuteScalar());
        return $"{prefix}-{DateTime.Today:yyyyMMdd}-{next:D5}";
    }

    private static string GetSupplierName(SqliteConnection conn, SqliteTransaction tx, int supplierId)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT Name FROM Suppliers WHERE Id=@id";
        cmd.Parameters.AddWithValue("@id", supplierId);
        return Convert.ToString(cmd.ExecuteScalar()) ?? string.Empty;
    }

    private static List<(int ProductId, int? VariantId, int Quantity, decimal UnitCost, int? BatchId)> ReadPurchaseOrderLines(
        SqliteConnection conn, SqliteTransaction tx, int orderId)
    {
        var lines = new List<(int, int?, int, decimal, int?)>();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"SELECT ProductId,VariantId,ReceivedQuantity,UnitCost,BatchId
                            FROM PurchaseOrderLines WHERE PurchaseOrderId=@id AND ReceivedQuantity>0";
        cmd.Parameters.AddWithValue("@id", orderId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lines.Add((reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.GetInt32(2), (decimal)reader.GetDouble(3), reader.IsDBNull(4) ? null : reader.GetInt32(4)));
        return lines;
    }

    private static List<(int ProductId, int? VariantId, int Quantity, decimal UnitPrice)> ReadSalesOrderLines(
        SqliteConnection conn, SqliteTransaction tx, int orderId)
    {
        var lines = new List<(int, int?, int, decimal)>();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"SELECT ProductId,VariantId,Quantity,UnitPrice
                            FROM SalesOrderLines WHERE SalesOrderId=@id";
        cmd.Parameters.AddWithValue("@id", orderId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lines.Add((reader.GetInt32(0), reader.IsDBNull(1) ? null : reader.GetInt32(1),
                reader.GetInt32(2), (decimal)reader.GetDouble(3)));
        return lines;
    }

    private void RecordPayment(string invoiceTable, string paymentTable, string partyColumn, int invoiceId,
                               decimal amount, string method, string reference, string notes)
    {
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), "Payment amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(method)) throw new ArgumentException("Payment method is required.");
        using var conn = _db.GetConnection();
        conn.Open();
        using var tx = conn.BeginTransaction();
        try
        {
            int partyId;
            decimal total;
            decimal returned;
            decimal paid;
            using (var invoice = conn.CreateCommand())
            {
                invoice.Transaction = tx;
                invoice.CommandText = $"SELECT {partyColumn},Total,ReturnedAmount,PaidAmount FROM {invoiceTable} WHERE Id=@id";
                invoice.Parameters.AddWithValue("@id", invoiceId);
                using var reader = invoice.ExecuteReader();
                if (!reader.Read()) throw new InvalidOperationException("Invoice was not found.");
                partyId = reader.GetInt32(0);
                total = (decimal)reader.GetDouble(1);
                returned = (decimal)reader.GetDouble(2);
                paid = (decimal)reader.GetDouble(3);
            }
            if (amount > total - returned - paid)
                throw new InvalidOperationException("Payment exceeds the invoice balance due.");
            using (var insert = conn.CreateCommand())
            {
                insert.Transaction = tx;
                insert.CommandText = $@"INSERT INTO {paymentTable}
                    ({partyColumn},{(invoiceTable == "SalesInvoices" ? "SalesInvoiceId" : "PurchaseInvoiceId")},Amount,PaymentDate,Method,Reference,Notes)
                    VALUES (@party,@invoice,@amount,@date,@method,@reference,@notes)";
                insert.Parameters.AddWithValue("@party", partyId);
                insert.Parameters.AddWithValue("@invoice", invoiceId);
                insert.Parameters.AddWithValue("@amount", amount);
                insert.Parameters.AddWithValue("@date", DateTime.Now.ToString("o"));
                insert.Parameters.AddWithValue("@method", method);
                insert.Parameters.AddWithValue("@reference", reference);
                insert.Parameters.AddWithValue("@notes", notes);
                insert.ExecuteNonQuery();
            }
            using (var update = conn.CreateCommand())
            {
                update.Transaction = tx;
                update.CommandText = $@"UPDATE {invoiceTable} SET PaidAmount=PaidAmount+@amount,
                    Status=CASE WHEN PaidAmount+@amount>=Total-ReturnedAmount THEN 'Paid' ELSE 'PartiallyPaid' END WHERE Id=@id";
                update.Parameters.AddWithValue("@amount", amount);
                update.Parameters.AddWithValue("@id", invoiceId);
                update.ExecuteNonQuery();
            }
            tx.Commit();
        }
        catch { tx.Rollback(); throw; }
    }

    private List<CommerceLine> GetInvoiceLines(string table, string parentColumn, int invoiceId, string priceColumn)
    {
        var lines = new List<CommerceLine>();
        using var conn = _db.GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"SELECT l.Id,l.ProductId,p.Name,l.VariantId,IFNULL(v.Attributes,''),l.Quantity,
                                    l.ReturnedQuantity,l.{priceColumn}
                             FROM {table} l JOIN Products p ON p.Id=l.ProductId
                             LEFT JOIN ProductVariants v ON v.Id=l.VariantId
                             WHERE l.{parentColumn}=@id ORDER BY l.Id";
        cmd.Parameters.AddWithValue("@id", invoiceId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            lines.Add(new CommerceLine
            {
                Id = reader.GetInt32(0), ProductId = reader.GetInt32(1), ProductName = reader.GetString(2),
                VariantId = reader.IsDBNull(3) ? null : reader.GetInt32(3), VariantName = reader.GetString(4),
                Quantity = reader.GetInt32(5), ReturnedQuantity = reader.GetInt32(6),
                UnitPrice = (decimal)reader.GetDouble(7)
            });
        return lines;
    }

    private List<PartyHistoryEntry> GetPartyHistory(bool supplier, int partyId)
    {
        var entries = new List<PartyHistoryEntry>();
        using var conn = _db.GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        var invoiceTable = supplier ? "PurchaseInvoices" : "SalesInvoices";
        var returnTable = supplier ? "PurchaseReturns" : "SalesReturns";
        var paymentsTable = supplier ? "SupplierPayments" : "CustomerPayments";
        var partyColumn = supplier ? "SupplierId" : "CustomerId";
        cmd.CommandText = $@"
            SELECT InvoiceDate, 'Invoice', Number, Status, Total-ReturnedAmount, PaidAmount FROM {invoiceTable} WHERE {partyColumn}=@id
            UNION ALL SELECT ReturnDate, 'Return', Number, 'Returned', Total, 0 FROM {returnTable} WHERE {partyColumn}=@id
            UNION ALL SELECT PaymentDate, 'Payment', IFNULL(Reference,''), Method, Amount, Amount FROM {paymentsTable} WHERE {partyColumn}=@id
            UNION ALL SELECT OrderDate, 'Order', Number, Status, 0, 0 FROM {(supplier ? "PurchaseOrders" : "SalesOrders")} WHERE {partyColumn}=@id
            ORDER BY 1 DESC";
        cmd.Parameters.AddWithValue("@id", partyId);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            entries.Add(new PartyHistoryEntry
            {
                Date = DateTime.Parse(reader.GetString(0)), RecordType = reader.GetString(1),
                Reference = reader.GetString(2), Status = reader.GetString(3),
                Amount = (decimal)reader.GetDouble(4), PaidAmount = (decimal)reader.GetDouble(5)
            });
        return entries;
    }

    private CommerceMetrics GetMetrics(string table, string netExpression)
    {
        using var conn = _db.GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
            SELECT IFNULL(SUM(CASE WHEN InvoiceDate>=@today THEN {netExpression} ELSE 0 END),0),
                   IFNULL(SUM(CASE WHEN InvoiceDate>=@month THEN {netExpression} ELSE 0 END),0),
                   IFNULL(SUM(MAX(0,{netExpression}-PaidAmount)),0),
                   IFNULL(SUM(ReturnedAmount),0),COUNT(*)
            FROM {table}";
        cmd.Parameters.AddWithValue("@today", DateTime.Today.ToString("o"));
        cmd.Parameters.AddWithValue("@month", new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1).ToString("o"));
        using var reader = cmd.ExecuteReader();
        reader.Read();
        return new CommerceMetrics
        {
            Today = (decimal)reader.GetDouble(0), Month = (decimal)reader.GetDouble(1),
            Outstanding = (decimal)reader.GetDouble(2), Returned = (decimal)reader.GetDouble(3),
            Documents = reader.GetInt32(4)
        };
    }

    private static void UpdateBatchQuantity(SqliteConnection conn, SqliteTransaction tx, int batchId, int delta)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE StockBatches SET Quantity=Quantity+@delta WHERE Id=@id AND Quantity+@delta>=0";
        cmd.Parameters.AddWithValue("@delta", delta);
        cmd.Parameters.AddWithValue("@id", batchId);
        if (cmd.ExecuteNonQuery() != 1)
            throw new InvalidOperationException("Batch quantity is insufficient or the batch is missing.");
    }

    private void ConsumeBatches(SqliteConnection conn, SqliteTransaction tx, int productId, int? variantId,
                                int warehouseId, int quantity, int invoiceLineId, int userId, string reference)
    {
        var available = new List<(int Id, string Number, int Quantity)>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"SELECT Id,BatchNumber,Quantity FROM StockBatches
                WHERE ProductId=@product AND WarehouseId=@warehouse AND Quantity>0
                  AND ((VariantId IS NULL AND @variant IS NULL) OR VariantId=@variant)
                  AND (ExpiryDate IS NULL OR ExpiryDate>=@today)
                ORDER BY ExpiryDate IS NULL,ExpiryDate,ReceivedAt,Id";
            cmd.Parameters.AddWithValue("@product", productId);
            cmd.Parameters.AddWithValue("@warehouse", warehouseId);
            cmd.Parameters.AddWithValue("@variant", (object?)variantId ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@today", DateTime.Today.ToString("o"));
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) available.Add((reader.GetInt32(0), reader.GetString(1), reader.GetInt32(2)));
        }
        if (available.Sum(batch => batch.Quantity) < quantity)
            throw new InvalidOperationException("There is not enough unexpired batch stock to complete this sale.");

        var remaining = quantity;
        foreach (var batch in available)
        {
            if (remaining == 0) break;
            var used = Math.Min(batch.Quantity, remaining);
            UpdateBatchQuantity(conn, tx, batch.Id, -used);
            var balance = _stock.ApplyDelta(conn, tx, productId, variantId, warehouseId, -used);
            using (var allocation = conn.CreateCommand())
            {
                allocation.Transaction = tx;
                allocation.CommandText = @"INSERT INTO SalesInvoiceLineBatches (SalesInvoiceLineId,BatchId,Quantity)
                                           VALUES (@line,@batch,@quantity)";
                allocation.Parameters.AddWithValue("@line", invoiceLineId);
                allocation.Parameters.AddWithValue("@batch", batch.Id);
                allocation.Parameters.AddWithValue("@quantity", used);
                allocation.ExecuteNonQuery();
            }
            _transactions.Insert(conn, tx, new Transaction
            {
                ProductId = productId, VariantId = variantId, Type = TransactionType.Sale,
                Quantity = -used, WarehouseId = warehouseId, BatchId = batch.Id,
                Reference = reference, Notes = $"Sales invoice (batch {batch.Number})",
                Date = DateTime.Now, UserId = userId, BalanceAfter = balance
            });
            remaining -= used;
        }
    }

    private static List<(int? BatchId, int Quantity)> ConsumeReturnBatchAllocations(
        SqliteConnection conn, SqliteTransaction tx, int invoiceLineId, int quantity)
    {
        var allocations = new List<(int? BatchId, int Quantity)>();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"SELECT BatchId,Quantity,ReturnedQuantity FROM SalesInvoiceLineBatches
                            WHERE SalesInvoiceLineId=@line ORDER BY Id";
        cmd.Parameters.AddWithValue("@line", invoiceLineId);
        using var reader = cmd.ExecuteReader();
        var rows = new List<(int BatchId, int Available)>();
        while (reader.Read()) rows.Add((reader.GetInt32(0), reader.GetInt32(1) - reader.GetInt32(2)));
        reader.Close();
        var remaining = quantity;
        foreach (var row in rows)
        {
            if (remaining == 0) break;
            var restored = Math.Min(row.Available, remaining);
            if (restored <= 0) continue;
            using var update = conn.CreateCommand();
            update.Transaction = tx;
            update.CommandText = @"UPDATE SalesInvoiceLineBatches SET ReturnedQuantity=ReturnedQuantity+@quantity
                                   WHERE SalesInvoiceLineId=@line AND BatchId=@batch";
            update.Parameters.AddWithValue("@quantity", restored);
            update.Parameters.AddWithValue("@line", invoiceLineId);
            update.Parameters.AddWithValue("@batch", row.BatchId);
            update.ExecuteNonQuery();
            allocations.Add((row.BatchId, restored));
            remaining -= restored;
        }
        if (remaining > 0) throw new InvalidOperationException("Sale has no remaining batch allocation for this return.");
        return allocations;
    }
}
