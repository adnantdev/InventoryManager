using System.Globalization;
using InventoryManager.Data;
using InventoryManager.Models;
using Microsoft.Data.Sqlite;

namespace InventoryManager.Services;

public sealed class ProductCsvService
{
    private readonly Database _database;
    private readonly InventoryService _inventory;
    private readonly AuthService _auth;

    public ProductCsvService(Database database, InventoryService inventory, AuthService auth)
    {
        _database = database;
        _inventory = inventory;
        _auth = auth;
    }

    public void Export(string path)
    {
        _auth.RequirePermission("inventory.read");
        using var writer = new StreamWriter(path, false, new System.Text.UTF8Encoding(true));
        writer.WriteLine("SKU,Barcode,Name,Description,CategoryId,UnitId,CostPrice,SellPrice,ReorderLevel,TrackBatches,TrackExpiry,HasVariants");
        foreach (var product in _inventory.Products.GetAll())
        {
            var fields = new[]
            {
                product.SKU, product.Barcode, product.Name, product.Description,
                product.CategoryId?.ToString(CultureInfo.InvariantCulture) ?? "",
                product.UnitId.ToString(CultureInfo.InvariantCulture),
                product.CostPrice.ToString(CultureInfo.InvariantCulture),
                product.SellPrice.ToString(CultureInfo.InvariantCulture),
                product.ReorderLevel.ToString(CultureInfo.InvariantCulture),
                product.TrackBatches ? "true" : "false",
                product.TrackExpiry ? "true" : "false",
                product.HasVariants ? "true" : "false"
            };
            writer.WriteLine(string.Join(",", fields.Select(Escape)));
        }
    }

    public ProductImportResult Import(string path)
    {
        _auth.RequirePermission("inventory.write");
        using var reader = new StreamReader(path);
        var header = reader.ReadLine() ?? throw new InvalidDataException("CSV file is empty.");
        var columns = ParseRow(header).Select((name, index) => (name: name.Trim(), index))
            .ToDictionary(item => item.name, item => item.index, StringComparer.OrdinalIgnoreCase);
        foreach (var required in new[] { "SKU", "Name" })
            if (!columns.ContainsKey(required)) throw new InvalidDataException($"CSV is missing required column '{required}'.");

        var result = new ProductImportResult();
        var lineNumber = 1;
        using var connection = _database.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        try
        {
            string? row;
            while ((row = reader.ReadLine()) != null)
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(row)) continue;
                var values = ParseRow(row);
                string Value(string key, string fallback = "")
                {
                    if (!columns.TryGetValue(key, out var index) || index >= values.Count) return fallback;
                    return values[index].Trim();
                }
                var sku = Value("SKU");
                var name = Value("Name");
                if (sku.Length == 0 || name.Length == 0)
                    throw new InvalidDataException($"Row {lineNumber}: SKU and Name are required.");
                var existingId = FindProductId(connection, transaction, sku);
                var unitId = ParseInt(Value("UnitId", "1"), 1, lineNumber, "UnitId");
                EnsureReference(connection, transaction, "UnitsOfMeasure", unitId, lineNumber, "UnitId");
                var categoryId = ParseNullableInt(Value("CategoryId"), lineNumber, "CategoryId");
                if (categoryId.HasValue)
                    EnsureReference(connection, transaction, "Categories", categoryId.Value, lineNumber, "CategoryId");
                var product = new Product
                {
                    SKU = sku, Name = name, Barcode = Value("Barcode"), Description = Value("Description"),
                    CategoryId = categoryId,
                    UnitId = unitId,
                    CostPrice = ParseDecimal(Value("CostPrice", "0"), 0, lineNumber, "CostPrice"),
                    SellPrice = ParseDecimal(Value("SellPrice", "0"), 0, lineNumber, "SellPrice"),
                    ReorderLevel = ParseInt(Value("ReorderLevel", "10"), 10, lineNumber, "ReorderLevel"),
                    TrackBatches = ParseBool(Value("TrackBatches")),
                    TrackExpiry = ParseBool(Value("TrackExpiry")),
                    HasVariants = ParseBool(Value("HasVariants"))
                };
                if (product.CostPrice < 0 || product.SellPrice < 0 || product.ReorderLevel < 0)
                    throw new InvalidDataException($"Row {lineNumber}: prices and reorder level cannot be negative.");
                if (existingId.HasValue)
                {
                    product.Id = existingId.Value;
                    SaveProduct(connection, transaction, product);
                    result.Updated++;
                }
                else
                {
                    SaveProduct(connection, transaction, product);
                    result.Imported++;
                }
                result.Rows++;
            }
            AuthService.WriteAudit(connection, _auth.CurrentUser?.Id, _auth.CurrentUser?.Username ?? string.Empty,
                "Import products", "Product", string.Empty, $"Rows={result.Rows}, Imported={result.Imported}, Updated={result.Updated}", transaction);
            transaction.Commit();
            return result;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    private static int? FindProductId(SqliteConnection connection, SqliteTransaction transaction, string sku)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id FROM Products WHERE SKU=@sku";
        command.Parameters.AddWithValue("@sku", sku);
        var result = command.ExecuteScalar();
        return result == null ? null : Convert.ToInt32(result);
    }

    private static void EnsureReference(SqliteConnection connection, SqliteTransaction transaction,
                                        string table, int id, int row, string field)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT COUNT(*) FROM {table} WHERE Id=@id";
        command.Parameters.AddWithValue("@id", id);
        if (Convert.ToInt32(command.ExecuteScalar()) == 0)
            throw new InvalidDataException($"Row {row}: '{field}' references an unknown record ({id}).");
    }

    private static void SaveProduct(SqliteConnection connection, SqliteTransaction transaction, Product product)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = product.Id == 0
            ? @"INSERT INTO Products
                    (SKU,Barcode,Name,Description,CategoryId,UnitId,CostPrice,SellPrice,ReorderLevel,
                     TrackBatches,TrackExpiry,HasVariants,IsActive,CreatedAt)
                VALUES (@sku,@barcode,@name,@description,@category,@unit,@cost,@sell,@reorder,
                        @batches,@expiry,@variants,1,@created)"
            : @"UPDATE Products SET Barcode=@barcode,Name=@name,Description=@description,CategoryId=@category,
                    UnitId=@unit,CostPrice=@cost,SellPrice=@sell,ReorderLevel=@reorder,
                    TrackBatches=@batches,TrackExpiry=@expiry,HasVariants=@variants WHERE Id=@id";
        command.Parameters.AddWithValue("@sku", product.SKU);
        command.Parameters.AddWithValue("@barcode", product.Barcode);
        command.Parameters.AddWithValue("@name", product.Name);
        command.Parameters.AddWithValue("@description", product.Description);
        command.Parameters.AddWithValue("@category", (object?)product.CategoryId ?? DBNull.Value);
        command.Parameters.AddWithValue("@unit", product.UnitId);
        command.Parameters.AddWithValue("@cost", product.CostPrice);
        command.Parameters.AddWithValue("@sell", product.SellPrice);
        command.Parameters.AddWithValue("@reorder", product.ReorderLevel);
        command.Parameters.AddWithValue("@batches", product.TrackBatches ? 1 : 0);
        command.Parameters.AddWithValue("@expiry", product.TrackExpiry ? 1 : 0);
        command.Parameters.AddWithValue("@variants", product.HasVariants ? 1 : 0);
        if (product.Id == 0) command.Parameters.AddWithValue("@created", DateTime.Now.ToString("o"));
        else command.Parameters.AddWithValue("@id", product.Id);
        command.ExecuteNonQuery();
    }

    private static string Escape(string value) => "\"" + value.Replace("\"", "\"\"") + "\"";

    private static List<string> ParseRow(string line)
    {
        var result = new List<string>();
        var field = new System.Text.StringBuilder();
        var quoted = false;
        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];
            if (character == '"')
            {
                if (quoted && i + 1 < line.Length && line[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else quoted = !quoted;
            }
            else if (character == ',' && !quoted)
            {
                result.Add(field.ToString());
                field.Clear();
            }
            else field.Append(character);
        }
        if (quoted) throw new InvalidDataException("CSV contains an unterminated quoted field.");
        result.Add(field.ToString());
        return result;
    }

    private static int ParseInt(string value, int fallback, int row, string field) =>
        value.Length == 0 ? fallback : int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : throw new InvalidDataException($"Row {row}: '{field}' must be a whole number.");

    private static int? ParseNullableInt(string value, int row, string field) =>
        value.Length == 0 ? null : ParseInt(value, 0, row, field);

    private static decimal ParseDecimal(string value, decimal fallback, int row, string field) =>
        value.Length == 0 ? fallback : decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
            ? parsed : throw new InvalidDataException($"Row {row}: '{field}' must be a number.");

    private static bool ParseBool(string value) => value.Equals("true", StringComparison.OrdinalIgnoreCase)
        || value.Equals("1", StringComparison.OrdinalIgnoreCase)
        || value.Equals("yes", StringComparison.OrdinalIgnoreCase);
}
