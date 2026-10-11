using InventoryManager.Data;
using InventoryManager.Models;

namespace InventoryManager.Services;

public sealed class AutomatedReorderService
{
    private readonly Database _database;
    private readonly AuthService _auth;

    public AutomatedReorderService(Database database, AuthService auth)
    {
        _database = database;
        _auth = auth;
    }

    public List<ReorderPolicy> GetPolicies()
    {
        _auth.RequirePermission("inventory.read");
        using var connection = _database.GetConnection();
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = @"SELECT p.Id,p.SKU,p.Name,IFNULL((SELECT SUM(Quantity) FROM StockLevels sl WHERE sl.ProductId=p.Id),0),
                    p.ReorderLevel,p.AutoReorderEnabled,p.ReorderLeadDays,p.ReorderSafetyDays,
                    IFNULL((SELECT SUM(l.Quantity-l.ReturnedQuantity) FROM SalesInvoiceLines l
                            JOIN SalesInvoices i ON i.Id=l.SalesInvoiceId
                            WHERE l.ProductId=p.Id AND i.InvoiceDate>=@since),0)
                FROM Products p WHERE p.IsActive=1 ORDER BY p.Name";
        command.Parameters.AddWithValue("@since", DateTime.Today.AddDays(-30).ToString("o"));
        using var reader = command.ExecuteReader();
        var policies = new List<ReorderPolicy>();
        while (reader.Read())
        {
            var sold = reader.GetInt32(8);
            var leadDays = reader.GetInt32(6);
            var safetyDays = reader.GetInt32(7);
            var suggested = (int)Math.Ceiling(sold / 30m * (leadDays + safetyDays));
            policies.Add(new ReorderPolicy
            {
                ProductId = reader.GetInt32(0),
                SKU = reader.GetString(1),
                ProductName = reader.GetString(2),
                StockQuantity = reader.GetInt32(3),
                ReorderLevel = reader.GetInt32(4),
                AutoReorderEnabled = reader.GetInt32(5) == 1,
                LeadDays = leadDays,
                SafetyDays = safetyDays,
                RecentUnitsSold = sold,
                SuggestedReorderLevel = suggested
            });
        }
        return policies;
    }

    public void SavePolicy(ReorderPolicy policy)
    {
        _auth.RequirePermission("inventory.write");
        if (policy.LeadDays is < 0 or > 365 || policy.SafetyDays is < 0 or > 365)
            throw new ArgumentOutOfRangeException(nameof(policy), "Lead and safety days must be between 0 and 365.");
        using var connection = _database.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"UPDATE Products SET AutoReorderEnabled=@enabled,ReorderLeadDays=@lead,
                               ReorderSafetyDays=@safety WHERE Id=@id";
        command.Parameters.AddWithValue("@enabled", policy.AutoReorderEnabled ? 1 : 0);
        command.Parameters.AddWithValue("@lead", policy.LeadDays);
        command.Parameters.AddWithValue("@safety", policy.SafetyDays);
        command.Parameters.AddWithValue("@id", policy.ProductId);
        if (command.ExecuteNonQuery() != 1) throw new InvalidOperationException("Product was not found.");
        AuthService.WriteAudit(connection, _auth.CurrentUser?.Id, _auth.CurrentUser?.Username ?? string.Empty,
            "Update reorder policy", "Product", policy.ProductId.ToString(),
            $"Enabled={policy.AutoReorderEnabled}, LeadDays={policy.LeadDays}, SafetyDays={policy.SafetyDays}", transaction);
        transaction.Commit();
    }

    public int RecalculateEnabledPolicies()
    {
        _auth.RequirePermission("inventory.write");
        using var connection = _database.GetConnection();
        connection.Open();
        using var transaction = connection.BeginTransaction();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = @"UPDATE Products SET ReorderLevel=MAX(1,CAST(ROUND(
                    IFNULL((SELECT SUM(l.Quantity-l.ReturnedQuantity)*1.0/30
                            FROM SalesInvoiceLines l JOIN SalesInvoices i ON i.Id=l.SalesInvoiceId
                            WHERE l.ProductId=Products.Id AND i.InvoiceDate>=@since),0)
                    *(ReorderLeadDays+ReorderSafetyDays),0) AS INTEGER))
                WHERE AutoReorderEnabled=1 AND IsActive=1";
        command.Parameters.AddWithValue("@since", DateTime.Today.AddDays(-30).ToString("o"));
        var affected = command.ExecuteNonQuery();
        AuthService.WriteAudit(connection, _auth.CurrentUser?.Id, _auth.CurrentUser?.Username ?? string.Empty,
            "Recalculate reorder levels", "Product", string.Empty, $"Updated {affected} policies", transaction);
        transaction.Commit();
        return affected;
    }
}
