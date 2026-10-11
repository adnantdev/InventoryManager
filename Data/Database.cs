using Microsoft.Data.Sqlite;

namespace InventoryManager.Data;

public class Database
{
    private readonly string _connectionString;

    public Database(string dbPath = "inventory.db")
    {
        _connectionString = $"Data Source={dbPath}";
        Initialize();
    }

    public SqliteConnection GetConnection() => new SqliteConnection(_connectionString);

    private void Initialize()
    {
        using var conn = GetConnection();
        conn.Open();
        using var cmd = conn.CreateCommand();

        cmd.CommandText = @"
        CREATE TABLE IF NOT EXISTS Users (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Username TEXT UNIQUE NOT NULL,
            PasswordHash TEXT NOT NULL,
            FullName TEXT,
            Role TEXT DEFAULT 'Staff',
            IsActive INTEGER NOT NULL DEFAULT 1,
            CreatedAt TEXT NOT NULL DEFAULT '',
            LastLoginAt TEXT NULL,
            MustChangePassword INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS RolePermissions (
            Role TEXT NOT NULL,
            Permission TEXT NOT NULL,
            PRIMARY KEY(Role, Permission)
        );

        CREATE TABLE IF NOT EXISTS AuditLog (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            UserId INTEGER NULL,
            Username TEXT NOT NULL DEFAULT '',
            Action TEXT NOT NULL,
            EntityType TEXT NOT NULL DEFAULT '',
            EntityId TEXT NOT NULL DEFAULT '',
            Details TEXT NOT NULL DEFAULT '',
            CreatedAt TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS AppSessions (
            SessionId TEXT PRIMARY KEY,
            UserId INTEGER NOT NULL,
            StartedAt TEXT NOT NULL,
            LastActivityAt TEXT NOT NULL,
            EndedAt TEXT NULL,
            MachineName TEXT NOT NULL,
            IsActive INTEGER NOT NULL DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS AppSettings (
            Key TEXT PRIMARY KEY,
            Value TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS LoginAttempts (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Username TEXT NOT NULL,
            AttemptAt TEXT NOT NULL,
            Succeeded INTEGER NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Categories (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            Description TEXT,
            ParentId INTEGER NULL
        );

        CREATE TABLE IF NOT EXISTS UnitsOfMeasure (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Code TEXT UNIQUE NOT NULL,
            Name TEXT NOT NULL,
            ConversionFactor REAL DEFAULT 1,
            BaseUnitId INTEGER NULL
        );

        CREATE TABLE IF NOT EXISTS Warehouses (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Code TEXT UNIQUE NOT NULL,
            Name TEXT NOT NULL,
            Address TEXT,
            IsDefault INTEGER DEFAULT 0,
            IsActive INTEGER DEFAULT 1,
            BranchId INTEGER NULL
        );

        CREATE TABLE IF NOT EXISTS Products (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            SKU TEXT UNIQUE NOT NULL,
            Barcode TEXT,
            Name TEXT NOT NULL,
            Description TEXT,
            CategoryId INTEGER NULL,
            UnitId INTEGER,
            CostPrice REAL DEFAULT 0,
            SellPrice REAL DEFAULT 0,
            ReorderLevel INTEGER DEFAULT 10,
            TrackBatches INTEGER DEFAULT 0,
            TrackExpiry INTEGER DEFAULT 0,
            HasVariants INTEGER DEFAULT 0,
            AutoReorderEnabled INTEGER NOT NULL DEFAULT 0,
            ReorderLeadDays INTEGER NOT NULL DEFAULT 14,
            ReorderSafetyDays INTEGER NOT NULL DEFAULT 7,
            IsActive INTEGER DEFAULT 1,
            CreatedAt TEXT
        );

        CREATE TABLE IF NOT EXISTS ProductVariants (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProductId INTEGER NOT NULL,
            SKU TEXT UNIQUE NOT NULL,
            Barcode TEXT,
            Attributes TEXT,
            PriceDelta REAL DEFAULT 0,
            IsActive INTEGER DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS StockLevels (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProductId INTEGER NOT NULL,
            VariantId INTEGER NULL,
            WarehouseId INTEGER NOT NULL,
            Quantity INTEGER DEFAULT 0,
            Reserved INTEGER DEFAULT 0,
            UNIQUE(ProductId, VariantId, WarehouseId)
        );

        CREATE TABLE IF NOT EXISTS StockBatches (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProductId INTEGER NOT NULL,
            VariantId INTEGER NULL,
            WarehouseId INTEGER NOT NULL,
            BatchNumber TEXT,
            LotNumber TEXT,
            Quantity INTEGER DEFAULT 0,
            ManufactureDate TEXT,
            ExpiryDate TEXT,
            CostPrice REAL DEFAULT 0,
            ReceivedAt TEXT,
            Supplier TEXT
        );

        CREATE TABLE IF NOT EXISTS Transactions (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            ProductId INTEGER NOT NULL,
            VariantId INTEGER NULL,
            Type INTEGER NOT NULL,
            Quantity INTEGER NOT NULL,
            WarehouseId INTEGER NOT NULL,
            BatchId INTEGER NULL,
            Reference TEXT,
            Notes TEXT,
            Date TEXT,
            UserId INTEGER,
            BalanceAfter INTEGER
        );

        CREATE TABLE IF NOT EXISTS StockTransfers (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Reference TEXT UNIQUE,
            ProductId INTEGER NOT NULL,
            VariantId INTEGER NULL,
            FromWarehouseId INTEGER NOT NULL,
            ToWarehouseId INTEGER NOT NULL,
            Quantity INTEGER NOT NULL,
            Status INTEGER DEFAULT 2,
            Notes TEXT,
            CreatedAt TEXT,
            UserId INTEGER
        );

        CREATE TABLE IF NOT EXISTS StockAdjustments (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Reference TEXT UNIQUE,
            ProductId INTEGER NOT NULL,
            VariantId INTEGER NULL,
            WarehouseId INTEGER NOT NULL,
            OldQuantity INTEGER,
            NewQuantity INTEGER,
            Reason INTEGER,
            Notes TEXT,
            CreatedAt TEXT,
            UserId INTEGER
        );

        CREATE TABLE IF NOT EXISTS Suppliers (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            Phone TEXT,
            Email TEXT,
            Address TEXT,
            TaxNumber TEXT,
            IsActive INTEGER DEFAULT 1,
            CreatedAt TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Customers (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            Phone TEXT,
            Email TEXT,
            Address TEXT,
            TaxNumber TEXT,
            IsActive INTEGER DEFAULT 1,
            CreatedAt TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS Branches (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Code TEXT UNIQUE NOT NULL,
            Name TEXT NOT NULL,
            Address TEXT,
            Phone TEXT,
            IsActive INTEGER NOT NULL DEFAULT 1,
            CreatedAt TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS NotificationEvents (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            EventKey TEXT NOT NULL UNIQUE,
            Status TEXT NOT NULL,
            Details TEXT NOT NULL DEFAULT '',
            CreatedAt TEXT NOT NULL
        );

        CREATE TABLE IF NOT EXISTS ApiKeys (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL,
            KeyHash TEXT NOT NULL,
            IsActive INTEGER NOT NULL DEFAULT 1,
            CreatedAt TEXT NOT NULL,
            LastUsedAt TEXT NULL
        );

        CREATE TABLE IF NOT EXISTS TaxRates (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT UNIQUE NOT NULL,
            Percent REAL NOT NULL DEFAULT 0,
            IsActive INTEGER DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS Discounts (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Name TEXT UNIQUE NOT NULL,
            Percent REAL NOT NULL DEFAULT 0,
            IsActive INTEGER DEFAULT 1
        );

        CREATE TABLE IF NOT EXISTS PurchaseOrders (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Number TEXT UNIQUE NOT NULL,
            SupplierId INTEGER NOT NULL,
            WarehouseId INTEGER NOT NULL,
            Status TEXT NOT NULL DEFAULT 'Open',
            OrderDate TEXT NOT NULL,
            Notes TEXT
        );

        CREATE TABLE IF NOT EXISTS PurchaseOrderLines (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            PurchaseOrderId INTEGER NOT NULL,
            ProductId INTEGER NOT NULL,
            VariantId INTEGER NULL,
            Quantity INTEGER NOT NULL,
            ReceivedQuantity INTEGER NOT NULL DEFAULT 0,
            UnitCost REAL NOT NULL,
            BatchId INTEGER NULL
        );

        CREATE TABLE IF NOT EXISTS PurchaseInvoices (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Number TEXT UNIQUE NOT NULL,
            SupplierId INTEGER NOT NULL,
            PurchaseOrderId INTEGER NULL,
            InvoiceDate TEXT NOT NULL,
            DueDate TEXT NULL,
            Subtotal REAL NOT NULL,
            DiscountPercent REAL NOT NULL DEFAULT 0,
            TaxPercent REAL NOT NULL DEFAULT 0,
            TaxAmount REAL NOT NULL DEFAULT 0,
            Total REAL NOT NULL,
            ReturnedAmount REAL NOT NULL DEFAULT 0,
            PaidAmount REAL NOT NULL DEFAULT 0,
            Status TEXT NOT NULL DEFAULT 'Unpaid',
            Notes TEXT
        );

        CREATE TABLE IF NOT EXISTS PurchaseInvoiceLines (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            PurchaseInvoiceId INTEGER NOT NULL,
            ProductId INTEGER NOT NULL,
            VariantId INTEGER NULL,
            Quantity INTEGER NOT NULL,
            ReturnedQuantity INTEGER NOT NULL DEFAULT 0,
            UnitCost REAL NOT NULL,
            BatchId INTEGER NULL
        );

        CREATE TABLE IF NOT EXISTS SupplierPayments (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            SupplierId INTEGER NOT NULL,
            PurchaseInvoiceId INTEGER NOT NULL,
            Amount REAL NOT NULL,
            PaymentDate TEXT NOT NULL,
            Method TEXT NOT NULL,
            Reference TEXT,
            Notes TEXT
        );

        CREATE TABLE IF NOT EXISTS PurchaseReturns (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Number TEXT UNIQUE NOT NULL,
            SupplierId INTEGER NOT NULL,
            PurchaseInvoiceId INTEGER NOT NULL,
            ReturnDate TEXT NOT NULL,
            Total REAL NOT NULL,
            Notes TEXT
        );

        CREATE TABLE IF NOT EXISTS PurchaseReturnLines (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            PurchaseReturnId INTEGER NOT NULL,
            PurchaseInvoiceLineId INTEGER NOT NULL,
            ProductId INTEGER NOT NULL,
            VariantId INTEGER NULL,
            Quantity INTEGER NOT NULL,
            UnitCost REAL NOT NULL,
            BatchId INTEGER NULL
        );

        CREATE TABLE IF NOT EXISTS SalesOrders (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Number TEXT UNIQUE NOT NULL,
            CustomerId INTEGER NOT NULL,
            WarehouseId INTEGER NOT NULL,
            Status TEXT NOT NULL DEFAULT 'Open',
            OrderDate TEXT NOT NULL,
            Notes TEXT
        );

        CREATE TABLE IF NOT EXISTS SalesOrderLines (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            SalesOrderId INTEGER NOT NULL,
            ProductId INTEGER NOT NULL,
            VariantId INTEGER NULL,
            Quantity INTEGER NOT NULL,
            UnitPrice REAL NOT NULL
        );

        CREATE TABLE IF NOT EXISTS SalesInvoices (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Number TEXT UNIQUE NOT NULL,
            SalesOrderId INTEGER NULL,
            CustomerId INTEGER NOT NULL,
            WarehouseId INTEGER NOT NULL,
            InvoiceDate TEXT NOT NULL,
            Subtotal REAL NOT NULL,
            DiscountPercent REAL NOT NULL DEFAULT 0,
            TaxPercent REAL NOT NULL DEFAULT 0,
            TaxAmount REAL NOT NULL DEFAULT 0,
            Total REAL NOT NULL,
            ReturnedAmount REAL NOT NULL DEFAULT 0,
            PaidAmount REAL NOT NULL DEFAULT 0,
            Status TEXT NOT NULL DEFAULT 'Unpaid',
            Notes TEXT
        );

        CREATE TABLE IF NOT EXISTS SalesInvoiceLines (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            SalesInvoiceId INTEGER NOT NULL,
            ProductId INTEGER NOT NULL,
            VariantId INTEGER NULL,
            Quantity INTEGER NOT NULL,
            ReturnedQuantity INTEGER NOT NULL DEFAULT 0,
            UnitPrice REAL NOT NULL,
            UnitCost REAL NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS SalesInvoiceLineBatches (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            SalesInvoiceLineId INTEGER NOT NULL,
            BatchId INTEGER NOT NULL,
            Quantity INTEGER NOT NULL,
            ReturnedQuantity INTEGER NOT NULL DEFAULT 0
        );

        CREATE TABLE IF NOT EXISTS CustomerPayments (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            CustomerId INTEGER NOT NULL,
            SalesInvoiceId INTEGER NOT NULL,
            Amount REAL NOT NULL,
            PaymentDate TEXT NOT NULL,
            Method TEXT NOT NULL,
            Reference TEXT,
            Notes TEXT
        );

        CREATE TABLE IF NOT EXISTS SalesReturns (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            Number TEXT UNIQUE NOT NULL,
            CustomerId INTEGER NOT NULL,
            SalesInvoiceId INTEGER NOT NULL,
            ReturnDate TEXT NOT NULL,
            Total REAL NOT NULL,
            Notes TEXT
        );

        CREATE TABLE IF NOT EXISTS SalesReturnLines (
            Id INTEGER PRIMARY KEY AUTOINCREMENT,
            SalesReturnId INTEGER NOT NULL,
            SalesInvoiceLineId INTEGER NOT NULL,
            ProductId INTEGER NOT NULL,
            VariantId INTEGER NULL,
            Quantity INTEGER NOT NULL,
            UnitPrice REAL NOT NULL
        );

        CREATE INDEX IF NOT EXISTS idx_tx_product ON Transactions(ProductId, Date);
        CREATE INDEX IF NOT EXISTS idx_levels ON StockLevels(ProductId, WarehouseId);
        CREATE INDEX IF NOT EXISTS idx_batch_expiry ON StockBatches(ExpiryDate);
        CREATE INDEX IF NOT EXISTS idx_purchase_orders_supplier ON PurchaseOrders(SupplierId, OrderDate);
        CREATE INDEX IF NOT EXISTS idx_purchase_invoices_supplier ON PurchaseInvoices(SupplierId, InvoiceDate);
        CREATE INDEX IF NOT EXISTS idx_sales_orders_customer ON SalesOrders(CustomerId, OrderDate);
        CREATE INDEX IF NOT EXISTS idx_sales_invoices_customer ON SalesInvoices(CustomerId, InvoiceDate);
        UPDATE StockLevels
        SET Quantity = (
                SELECT SUM(duplicate.Quantity) FROM StockLevels duplicate
                WHERE duplicate.ProductId = StockLevels.ProductId
                  AND duplicate.WarehouseId = StockLevels.WarehouseId
                  AND duplicate.VariantId IS NULL
            ),
            Reserved = (
                SELECT SUM(duplicate.Reserved) FROM StockLevels duplicate
                WHERE duplicate.ProductId = StockLevels.ProductId
                  AND duplicate.WarehouseId = StockLevels.WarehouseId
                  AND duplicate.VariantId IS NULL
            )
        WHERE VariantId IS NULL
          AND Id = (
                SELECT MIN(duplicate.Id) FROM StockLevels duplicate
                WHERE duplicate.ProductId = StockLevels.ProductId
                  AND duplicate.WarehouseId = StockLevels.WarehouseId
                  AND duplicate.VariantId IS NULL
            );
        DELETE FROM StockLevels
        WHERE VariantId IS NULL
          AND Id NOT IN (
                SELECT MIN(Id) FROM StockLevels
                WHERE VariantId IS NULL
                GROUP BY ProductId, WarehouseId
            );
        CREATE UNIQUE INDEX IF NOT EXISTS idx_levels_unvarianted
            ON StockLevels(ProductId, WarehouseId) WHERE VariantId IS NULL;
        ";

        cmd.ExecuteNonQuery();

        EnsureColumn(conn, "Users", "IsActive", "INTEGER NOT NULL DEFAULT 1");
        EnsureColumn(conn, "Users", "CreatedAt", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn(conn, "Users", "LastLoginAt", "TEXT NULL");
        EnsureColumn(conn, "Users", "MustChangePassword", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(conn, "Warehouses", "BranchId", "INTEGER NULL");
        EnsureColumn(conn, "Products", "AutoReorderEnabled", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn(conn, "Products", "ReorderLeadDays", "INTEGER NOT NULL DEFAULT 14");
        EnsureColumn(conn, "Products", "ReorderSafetyDays", "INTEGER NOT NULL DEFAULT 7");
        using (var seed = conn.CreateCommand())
        {
            seed.CommandText = "UPDATE Users SET CreatedAt=@now WHERE CreatedAt=''; UPDATE Users SET MustChangePassword=1 WHERE Username='admin' AND LastLoginAt IS NULL; INSERT OR IGNORE INTO Branches (Code,Name,Address,Phone,IsActive,CreatedAt) VALUES ('HQ','Head Office','', '',1,@now);";
            seed.Parameters.AddWithValue("@now", DateTime.Now.ToString("o"));
            seed.ExecuteNonQuery();
        }

        // ---------- Seed defaults ----------
        cmd.CommandText = "SELECT COUNT(*) FROM UnitsOfMeasure";
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
        {
            cmd.CommandText = @"INSERT INTO UnitsOfMeasure (Code, Name, ConversionFactor) VALUES
                ('pcs','Pieces',1),('box','Box',1),('kg','Kilogram',1),
                ('g','Gram',1),('ltr','Liter',1),('ml','Milliliter',1),
                ('m','Meter',1),('pack','Pack',1)";
            cmd.ExecuteNonQuery();
        }

        SeedPermissions(conn);

        cmd.CommandText = "SELECT COUNT(*) FROM TaxRates";
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
        {
            cmd.CommandText = @"INSERT INTO TaxRates (Name, Percent) VALUES
                ('No tax',0),('Tax 5%',5),('Tax 12%',12),('Tax 18%',18),('Tax 28%',28)";
            cmd.ExecuteNonQuery();
        }

        cmd.CommandText = "SELECT COUNT(*) FROM Discounts";
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
        {
            cmd.CommandText = "INSERT INTO Discounts (Name, Percent) VALUES ('No discount',0)";
            cmd.ExecuteNonQuery();
        }

        cmd.CommandText = "SELECT COUNT(*) FROM Warehouses";
        if (Convert.ToInt64(cmd.ExecuteScalar()) == 0)
        {
            cmd.CommandText = @"INSERT INTO Warehouses (Code, Name, Address, IsDefault) VALUES
                ('WH-MAIN','Main Warehouse','Headquarters',1),
                ('WH-STORE','Retail Store','Downtown',0)";
            cmd.ExecuteNonQuery();
        }
    }

    public string DatabasePath => new SqliteConnectionStringBuilder(_connectionString).DataSource;

        private static void EnsureColumn(SqliteConnection connection, string table, string column, string definition)
        {
            using var check = connection.CreateCommand();
            check.CommandText = $"PRAGMA table_info({table})";
            using var reader = check.ExecuteReader();
            while (reader.Read())
            {
                if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            reader.Close();
            using var alter = connection.CreateCommand();
            alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
            alter.ExecuteNonQuery();
        }

        private static void SeedPermissions(SqliteConnection connection)
        {
            var permissions = new[]
            {
                "inventory.read", "inventory.write", "commerce.read", "commerce.write",
                "reports.read", "users.manage", "admin.manage", "backup.manage", "audit.read"
            };
            using var cmd = connection.CreateCommand();
            cmd.CommandText = "INSERT OR IGNORE INTO RolePermissions (Role,Permission) VALUES ('Admin',@permission);";
            cmd.Parameters.Add("@permission", SqliteType.Text);
            foreach (var permission in permissions)
            {
                cmd.Parameters["@permission"].Value = permission;
                cmd.ExecuteNonQuery();
            }
            cmd.Parameters.Clear();
            cmd.CommandText = "INSERT OR IGNORE INTO RolePermissions (Role,Permission) VALUES ('Staff',@permission);";
            cmd.Parameters.Add("@permission", SqliteType.Text);
            foreach (var permission in new[] { "inventory.read", "inventory.write", "commerce.read", "commerce.write", "reports.read" })
            {
                cmd.Parameters["@permission"].Value = permission;
                cmd.ExecuteNonQuery();
            }
        }
    }