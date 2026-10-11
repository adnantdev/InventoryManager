using System.ComponentModel;
using InventoryManager.Models;
using InventoryManager.Services;

namespace InventoryManager.Forms;

public sealed class PurchasingPage : UserControl
{
    private readonly InventoryService _inventory;
    private readonly CommerceService _commerce;
    private readonly AuthService _auth;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _suppliersGrid = CommerceVisuals.Grid();
    private readonly DataGridView _supplierHistoryGrid = CommerceVisuals.Grid();
    private readonly DataGridView _ordersGrid = CommerceVisuals.Grid();
    private readonly DataGridView _invoicesGrid = CommerceVisuals.Grid();
    private readonly DataGridView _invoiceLinesGrid = CommerceVisuals.Grid();
    private readonly DataGridView _ratesGrid = CommerceVisuals.Grid();
    private readonly DataGridView _cartGrid = CommerceVisuals.Grid();
    private readonly BindingList<CommerceOrderLineInput> _cart = new();
    private readonly ComboBox _supplier = Dropdown();
    private readonly ComboBox _warehouse = Dropdown();
    private readonly ComboBox _product = Dropdown();
    private readonly ComboBox _variant = Dropdown();
    private readonly ComboBox _taxRate = Dropdown();
    private readonly ComboBox _discount = Dropdown();
    private readonly NumericUpDown _quantity = new() { Minimum = 1, Maximum = 100000, Value = 1, Width = 90 };
    private readonly NumericUpDown _unitCost = new() { Minimum = 0, Maximum = 100000000, DecimalPlaces = 2, Width = 110 };

    public PurchasingPage(InventoryService inventory, CommerceService commerce, AuthService auth)
    {
        _inventory = inventory;
        _commerce = commerce;
        _auth = auth;
        Dock = DockStyle.Fill;
        BackColor = CommerceVisuals.Canvas;
        Controls.Add(_tabs);
        _tabs.TabPages.Add(BuildDashboardTab());
        _tabs.TabPages.Add(BuildSuppliersTab());
        _tabs.TabPages.Add(BuildOrdersTab());
        _tabs.TabPages.Add(BuildInvoicesTab());
        _tabs.TabPages.Add(BuildRatesTab());
        RefreshAll();
    }

    private TabPage BuildDashboardTab()
    {
        var page = new TabPage("Overview");
        var metrics = _commerce.GetPurchaseMetrics();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        for (var i = 0; i < 4; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        cards.Controls.Add(CommerceVisuals.Card("Purchases today", metrics.Today.ToString("C"), Color.FromArgb(0, 150, 136)), 0, 0);
        cards.Controls.Add(CommerceVisuals.Card("Purchases this month", metrics.Month.ToString("C"), CommerceVisuals.Accent), 1, 0);
        cards.Controls.Add(CommerceVisuals.Card("Supplier balance", metrics.Outstanding.ToString("C"), Color.FromArgb(226, 145, 33)), 2, 0);
        cards.Controls.Add(CommerceVisuals.Card("Total returns", metrics.Returned.ToString("C"), Color.FromArgb(201, 78, 78)), 3, 0);
        var chartPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = Color.White };
        var chart = new BarChartControl { Dock = DockStyle.Fill };
        var report = _commerce.GetPurchaseReport(DateTime.Today.AddDays(-180), DateTime.Today.AddDays(1));
        chart.SetData(report.GroupBy(row => row.Date.ToString("MMM yy"))
            .OrderBy(group => group.Min(row => row.Date)).Select(group => (group.Key, group.Sum(row => row.Revenue))).TakeLast(10));
        chartPanel.Controls.Add(chart);
        root.Controls.Add(cards, 0, 0);
        root.Controls.Add(chartPanel, 0, 1);
        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildSuppliersTab()
    {
        var page = new TabPage("Suppliers & history");
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 390 };
        var left = new Panel { Dock = DockStyle.Fill };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44 };
        buttons.Controls.Add(CommerceVisuals.Button("Add supplier", (_, _) => EditSupplier(null)));
        buttons.Controls.Add(CommerceVisuals.Button("Edit selected", (_, _) => EditSupplier(Selected<Supplier>(_suppliersGrid))));
        left.Controls.Add(_suppliersGrid);
        left.Controls.Add(buttons);
        _suppliersGrid.SelectionChanged += (_, _) => LoadSupplierHistory();
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(_supplierHistoryGrid);
        page.Controls.Add(split);
        return page;
    }

    private TabPage BuildOrdersTab()
    {
        var page = new TabPage("Purchase orders & receiving");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 155));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var composer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
        AddLabeled(composer, "Supplier", _supplier);
        AddLabeled(composer, "Warehouse", _warehouse);
        AddLabeled(composer, "Product", _product);
        AddLabeled(composer, "Variant", _variant);
        AddLabeled(composer, "Quantity", _quantity);
        AddLabeled(composer, "Unit cost", _unitCost);
        AddLabeled(composer, "Discount", _discount);
        AddLabeled(composer, "Tax", _taxRate);
        composer.Controls.Add(CommerceVisuals.Button("Add line", (_, _) => Run(AddCartLine)));
        var cartPanel = new Panel { Dock = DockStyle.Fill };
        var cartActions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
        cartActions.Controls.Add(CommerceVisuals.Button("Create purchase order", (_, _) => Run(CreateOrder)));
        cartActions.Controls.Add(CommerceVisuals.Button("Remove selected line", (_, _) => RemoveCartLine()));
        cartPanel.Controls.Add(_cartGrid);
        cartPanel.Controls.Add(cartActions);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44 };
        actions.Controls.Add(CommerceVisuals.Button("Receive selected", (_, _) => Run(ReceiveOrder)));
        actions.Controls.Add(CommerceVisuals.Button("Create invoice", (_, _) => Run(CreateInvoice)));
        root.Controls.Add(composer, 0, 0);
        root.Controls.Add(cartPanel, 0, 1);
        root.Controls.Add(_ordersGrid, 0, 2);
        page.Controls.Add(root);
        page.Controls.Add(actions);
        _cartGrid.DataSource = _cart;
        _product.SelectedIndexChanged += (_, _) => UpdateVariants(_product, _variant, _unitCost);
        return page;
    }

    private TabPage BuildInvoicesTab()
    {
        var page = new TabPage("Invoices, payments & returns");
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 310 };
        split.Panel1.Controls.Add(_invoicesGrid);
        split.Panel2.Controls.Add(_invoiceLinesGrid);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44 };
        actions.Controls.Add(CommerceVisuals.Button("Record supplier payment", (_, _) => Run(RecordPayment)));
        actions.Controls.Add(CommerceVisuals.Button("Return selected line", (_, _) => Run(ReturnLine)));
        page.Controls.Add(split);
        page.Controls.Add(actions);
        _invoicesGrid.SelectionChanged += (_, _) => LoadPurchaseInvoiceLines();
        return page;
    }

    private TabPage BuildRatesTab()
    {
        var page = new TabPage("Tax & discounts");
        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16) };
        panel.Controls.Add(new Label { Text = "Tax rates and purchase discounts are percentage-based and stored for reuse.", AutoSize = true, Margin = new Padding(5, 12, 12, 12) });
        panel.Controls.Add(CommerceVisuals.Button("Add tax rate", (_, _) => Run(() => EditRate(false))));
        panel.Controls.Add(CommerceVisuals.Button("Add discount", (_, _) => Run(() => EditRate(true))));
        page.Controls.Add(_ratesGrid);
        page.Controls.Add(panel);
        return page;
    }

    private void RefreshAll()
    {
        _suppliersGrid.DataSource = _commerce.GetSuppliers();
        _ordersGrid.DataSource = _commerce.GetPurchaseOrders();
        _invoicesGrid.DataSource = _commerce.GetPurchaseInvoices();
        _ratesGrid.DataSource = _commerce.GetTaxRates()
            .Select(rate => new RateDisplay("Tax", rate.Name, rate.Percent))
            .Concat(_commerce.GetDiscounts().Select(rate => new RateDisplay("Discount", rate.Name, rate.Percent)))
            .ToList();
        Bind(_supplier, _commerce.GetSuppliers());
        Bind(_warehouse, _inventory.Warehouses.GetAll());
        Bind(_product, _inventory.Products.GetAll());
        Bind(_taxRate, _commerce.GetTaxRates());
        Bind(_discount, _commerce.GetDiscounts());
        LoadSupplierHistory();
        LoadPurchaseInvoiceLines();
    }

    private void EditSupplier(Supplier? selected)
    {
        var values = CommerceDialogs.Prompt(FindForm(), selected == null ? "Add supplier" : "Edit supplier",
            ("Name", selected?.Name ?? ""), ("Phone", selected?.Phone ?? ""), ("Email", selected?.Email ?? ""),
            ("Address", selected?.Address ?? ""), ("Tax number", selected?.TaxNumber ?? ""));
        if (values == null) return;
        Run(() =>
        {
            _commerce.SaveSupplier(new Supplier
            {
                Id = selected?.Id ?? 0, Name = values[0], Phone = values[1], Email = values[2],
                Address = values[3], TaxNumber = values[4]
            });
            RefreshAll();
        });
    }

    private void CreateOrder()
    {
        var supplier = _supplier.SelectedItem as Supplier ?? throw new InvalidOperationException("Select a supplier.");
        var warehouse = _warehouse.SelectedItem as Models.Warehouse ?? throw new InvalidOperationException("Select a warehouse.");
        if (_cart.Count == 0) throw new InvalidOperationException("Add at least one product line.");
        _commerce.CreatePurchaseOrder(supplier.Id, warehouse.Id, _cart.ToList(), "");
        _cart.Clear();
        RefreshAll();
    }

    private void AddCartLine()
    {
        var product = _product.SelectedItem as Product ?? throw new InvalidOperationException("Select a product.");
        var variant = _variant.SelectedItem as ProductVariant;
        if (product.HasVariants && variant == null)
            throw new InvalidOperationException("Select a product variant.");
        _cart.Add(new CommerceOrderLineInput
        {
            ProductId = product.Id,
            ProductName = product.Name,
            VariantId = variant?.Id,
            VariantName = variant?.Attributes ?? "",
            Quantity = decimal.ToInt32(_quantity.Value),
            UnitPrice = _unitCost.Value
        });
    }

    private void RemoveCartLine()
    {
        if (_cartGrid.CurrentRow?.DataBoundItem is CommerceOrderLineInput line)
            _cart.Remove(line);
    }

    private void ReceiveOrder()
    {
        var order = Selected<PurchaseOrderSummary>(_ordersGrid) ?? throw new InvalidOperationException("Select a purchase order.");
        var values = CommerceDialogs.Prompt(FindForm(), "Receive goods",
            ("Batch number (optional)", ""), ("Lot number (optional)", ""), ("Expiry (YYYY-MM-DD)", ""));
        if (values == null) return;
        DateTime? expiry = null;
        if (!string.IsNullOrWhiteSpace(values[2]))
        {
            if (!DateTime.TryParse(values[2], out var parsed)) throw new FormatException("Expiry date is invalid.");
            expiry = parsed;
        }
        _commerce.ReceivePurchaseOrder(order.Id, _auth.CurrentUser?.Id ?? 0, values[0], values[1], expiry);
        RefreshAll();
    }

    private void CreateInvoice()
    {
        var order = Selected<PurchaseOrderSummary>(_ordersGrid) ?? throw new InvalidOperationException("Select a purchase order.");
        var discount = _discount.SelectedItem as TaxRate;
        var tax = _taxRate.SelectedItem as TaxRate;
        _commerce.CreatePurchaseInvoiceFromOrder(order.Id, discount?.Percent ?? 0, tax?.Percent ?? 0, DateTime.Today.AddDays(30));
        RefreshAll();
        _tabs.SelectedIndex = 3;
    }

    private void RecordPayment()
    {
        var invoice = Selected<PurchaseInvoiceSummary>(_invoicesGrid) ?? throw new InvalidOperationException("Select an invoice.");
        var values = CommerceDialogs.Prompt(FindForm(), $"Payment for {invoice.Number}",
            ("Amount", invoice.BalanceDue.ToString("0.00")), ("Method", "Cash"), ("Reference", ""), ("Notes", ""));
        if (values == null) return;
        if (!decimal.TryParse(values[0], out var amount)) throw new FormatException("Enter a valid payment amount.");
        _commerce.RecordSupplierPayment(invoice.Id, amount, values[1], values[2], values[3]);
        RefreshAll();
    }

    private void ReturnLine()
    {
        var invoice = Selected<PurchaseInvoiceSummary>(_invoicesGrid) ?? throw new InvalidOperationException("Select an invoice.");
        var line = Selected<CommerceLine>(_invoiceLinesGrid) ?? throw new InvalidOperationException("Select an invoice line.");
        var values = CommerceDialogs.Prompt(FindForm(), $"Return {line.ProductName}", ("Quantity", "1"), ("Reason", ""));
        if (values == null) return;
        if (!int.TryParse(values[0], out var quantity)) throw new FormatException("Enter a valid return quantity.");
        _commerce.ReturnPurchaseInvoice(invoice.Id, line.Id, quantity, _auth.CurrentUser?.Id ?? 0, values[1]);
        RefreshAll();
    }

    private void EditRate(bool isDiscount)
    {
        var values = CommerceDialogs.Prompt(FindForm(), isDiscount ? "Add discount" : "Add tax rate",
            ("Name", ""), ("Percent", "0"));
        if (values == null) return;
        if (!decimal.TryParse(values[1], out var percent)) throw new FormatException("Enter a valid percentage.");
        var rate = new TaxRate { Name = values[0], Percent = percent };
        if (isDiscount) _commerce.SaveDiscount(rate);
        else _commerce.SaveTaxRate(rate);
        RefreshAll();
    }

    private void LoadSupplierHistory()
    {
        var supplier = Selected<Supplier>(_suppliersGrid);
        _supplierHistoryGrid.DataSource = supplier == null ? null : _commerce.GetSupplierHistory(supplier.Id);
    }

    private void LoadPurchaseInvoiceLines()
    {
        var invoice = Selected<PurchaseInvoiceSummary>(_invoicesGrid);
        _invoiceLinesGrid.DataSource = invoice == null ? null : _commerce.GetPurchaseInvoiceLines(invoice.Id);
    }

    private void UpdateVariants(ComboBox productBox, ComboBox variantBox, NumericUpDown price)
    {
        var product = productBox.SelectedItem as Product;
        var variants = product == null ? new List<ProductVariant>() : _inventory.Products.GetVariants(product.Id).Where(item => item.IsActive).ToList();
        Bind(variantBox, variants);
        if (product != null) price.Value = Math.Clamp(product.CostPrice, price.Minimum, price.Maximum);
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception exception) { CommerceDialogs.ShowError(FindForm(), exception); }
    }

    private static T? Selected<T>(DataGridView grid) where T : class =>
        grid.CurrentRow?.DataBoundItem as T;

    private static ComboBox Dropdown() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };

    private static void AddLabeled(FlowLayoutPanel panel, string label, Control input)
    {
        var group = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, Margin = new Padding(5) };
        group.Controls.Add(new Label { Text = label, AutoSize = true });
        group.Controls.Add(input);
        panel.Controls.Add(group);
    }

    private static void Bind<T>(ComboBox combo, IEnumerable<T> values)
    {
        combo.DataSource = null;
        combo.DataSource = values.ToList();
    }

    private sealed record RateDisplay(string Type, string Name, decimal Percent);
}
