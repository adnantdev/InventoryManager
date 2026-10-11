namespace InventoryManager.Models;

public sealed class ReorderPolicy
{
    public int ProductId { get; set; }
    public string SKU { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int StockQuantity { get; set; }
    public int ReorderLevel { get; set; }
    public bool AutoReorderEnabled { get; set; }
    public int LeadDays { get; set; }
    public int SafetyDays { get; set; }
    public int SuggestedReorderLevel { get; set; }
    public int RecentUnitsSold { get; set; }
}

public sealed class ProductImportResult
{
    public int Imported { get; set; }
    public int Updated { get; set; }
    public int Rows { get; set; }
}
