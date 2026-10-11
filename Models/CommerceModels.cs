namespace InventoryManager.Models;

public class Supplier
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string TaxNumber { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public override string ToString() => Name;
}

public class Customer
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string TaxNumber { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;

    public override string ToString() => Name;
}

public class CommerceLine
{
    public int Id { get; set; }
    public int DocumentId { get; set; }
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int? VariantId { get; set; }
    public string VariantName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public int ReturnedQuantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal => Quantity * UnitPrice;
}

public class CommerceOrderLineInput
{
    public int ProductId { get; set; }
    public string ProductName { get; set; } = string.Empty;
    public int? VariantId { get; set; }
    public string VariantName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }
    public decimal LineTotal => Quantity * UnitPrice;
}

public class PurchaseOrderSummary
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public int LineCount { get; set; }
    public decimal Subtotal { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class PurchaseInvoiceSummary
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public int SupplierId { get; set; }
    public string SupplierName { get; set; } = string.Empty;
    public int? PurchaseOrderId { get; set; }
    public DateTime InvoiceDate { get; set; }
    public DateTime? DueDate { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public decimal ReturnedAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceDue => Math.Max(0, Total - ReturnedAmount - PaidAmount);
    public string Status { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public class SalesOrderSummary
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int WarehouseId { get; set; }
    public string WarehouseName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime OrderDate { get; set; }
    public int LineCount { get; set; }
    public decimal Subtotal { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class SalesInvoiceSummary
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public int CustomerId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public int? SalesOrderId { get; set; }
    public int WarehouseId { get; set; }
    public DateTime InvoiceDate { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal TaxPercent { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal Total { get; set; }
    public decimal ReturnedAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceDue => Math.Max(0, Total - ReturnedAmount - PaidAmount);
    public string Status { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public class PartyHistoryEntry
{
    public DateTime Date { get; set; }
    public string RecordType { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceDue => Math.Max(0, Amount - PaidAmount);
}

public class TaxRate
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Percent { get; set; }
    public bool IsActive { get; set; } = true;

    public override string ToString() => $"{Name} ({Percent:0.##}%)";
}

public class CommerceMetrics
{
    public decimal Today { get; set; }
    public decimal Month { get; set; }
    public decimal Outstanding { get; set; }
    public decimal Returned { get; set; }
    public int Documents { get; set; }
}

public class CommerceReportRow
{
    public DateTime Date { get; set; }
    public string Reference { get; set; } = string.Empty;
    public string Party { get; set; } = string.Empty;
    public string Product { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Revenue { get; set; }
    public decimal Cost { get; set; }
    public decimal Tax { get; set; }
    public decimal Profit => Revenue - Cost;
}
