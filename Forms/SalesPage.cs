using System.ComponentModel;
using InventoryManager.Models;
using InventoryManager.Services;

namespace InventoryManager.Forms;

public sealed class SalesPage : UserControl
{
    private readonly InventoryService _inventory;
    private readonly CommerceService _commerce;
    private readonly AuthService _auth;
    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly DataGridView _customersGrid = CommerceVisuals.Grid();
    private readonly DataGridView _customerHistoryGrid = CommerceVisuals.Grid();
    private readonly DataGridView _ordersGrid = CommerceVisuals.Grid();
    private readonly DataGridView _invoicesGrid = CommerceVisuals.Grid();
    private readonly DataGridView _invoiceLinesGrid = CommerceVisuals.Grid();
    private readonly DataGridView _ratesGrid = CommerceVisuals.Grid();
    private readonly DataGridView _cartGrid = CommerceVisuals.Grid();
    private readonly BindingList<CommerceOrderLineInput> _cart = new();
    private readonly ComboBox _customer = Dropdown();
    private readonly ComboBox _warehouse = Dropdown();
    private readonly ComboBox _product = Dropdown();
    private readonly ComboBox _variant = Dropdown();
    private readonly ComboBox _taxRate = Dropdown();
    private readonly ComboBox _discount = Dropdown();
    private readonly NumericUpDown _quantity = new() { Minimum = 1, Maximum = 100000, Value = 1, Width = 90 };
    private readonly NumericUpDown _unitPrice = new() { Minimum = 0, Maximum = 100000000, DecimalPlaces = 2, Width = 110 };

    public SalesPage(InventoryService inventory, CommerceService commerce, AuthService auth)
    {
        _inventory = inventory;
        _commerce = commerce;
        _auth = auth;
        Dock = DockStyle.Fill;
        BackColor = CommerceVisuals.Canvas;
        Controls.Add(_tabs);
        _tabs.TabPages.Add(BuildDashboardTab());
        _tabs.TabPages.Add(BuildCustomersTab());
        _tabs.TabPages.Add(BuildOrdersTab());
        _tabs.TabPages.Add(BuildInvoicesTab());
        _tabs.TabPages.Add(BuildRatesTab());
        RefreshAll();
    }

    private TabPage BuildDashboardTab()
    {
        var page = new TabPage("Overview");
        var metrics = _commerce.GetSalesMetrics();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Padding = new Padding(12) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 130));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
        for (var i = 0; i < 4; i++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        cards.Controls.Add(CommerceVisuals.Card("Sales today", metrics.Today.ToString("C"), Color.FromArgb(0, 150, 136)), 0, 0);
        cards.Controls.Add(CommerceVisuals.Card("Sales this month", metrics.Month.ToString("C"), CommerceVisuals.Accent), 1, 0);
        cards.Controls.Add(CommerceVisuals.Card("Customer balance", metrics.Outstanding.ToString("C"), Color.FromArgb(226, 145, 33)), 2, 0);
        cards.Controls.Add(CommerceVisuals.Card("Total returns", metrics.Returned.ToString("C"), Color.FromArgb(201, 78, 78)), 3, 0);
        var chartPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = Color.White };
        var chart = new BarChartControl { Dock = DockStyle.Fill };
        var report = _commerce.GetSalesReport(DateTime.Today.AddDays(-180), DateTime.Today.AddDays(1));
        chart.SetData(report.GroupBy(row => row.Date.ToString("MMM yy"))
            .OrderBy(group => group.Min(row => row.Date)).Select(group => (group.Key, group.Sum(row => row.Revenue))).TakeLast(10));
        chartPanel.Controls.Add(chart);
        root.Controls.Add(cards, 0, 0);
        root.Controls.Add(chartPanel, 0, 1);
        page.Controls.Add(root);
        return page;
    }

    private TabPage BuildCustomersTab()
    {
        var page = new TabPage("Customers & history");
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterDistance = 390 };
        var left = new Panel { Dock = DockStyle.Fill };
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44 };
        buttons.Controls.Add(CommerceVisuals.Button("Add customer", (_, _) => EditCustomer(null)));
        buttons.Controls.Add(CommerceVisuals.Button("Edit selected", (_, _) => EditCustomer(Selected<Customer>(_customersGrid))));
        left.Controls.Add(_customersGrid);
        left.Controls.Add(buttons);
        _customersGrid.SelectionChanged += (_, _) => LoadCustomerHistory();
        split.Panel1.Controls.Add(left);
        split.Panel2.Controls.Add(_customerHistoryGrid);
        page.Controls.Add(split);
        return page;
    }

    private TabPage BuildOrdersTab()
    {
        var page = new TabPage("Sales orders & POS");
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 155));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var composer = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
        AddLabeled(composer, "Customer", _customer);
        AddLabeled(composer, "Warehouse", _warehouse);
        AddLabeled(composer, "Product", _product);
        AddLabeled(composer, "Variant", _variant);
        AddLabeled(composer, "Quantity", _quantity);
        AddLabeled(composer, "Unit price", _unitPrice);
        AddLabeled(composer, "Discount", _discount);
        AddLabeled(composer, "Tax", _taxRate);
        composer.Controls.Add(CommerceVisuals.Button("Add line", (_, _) => Run(AddCartLine)));
        var cartPanel = new Panel { Dock = DockStyle.Fill };
        var cartActions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 42 };
        cartActions.Controls.Add(CommerceVisuals.Button("Create sales order", (_, _) => Run(CreateOrder)));
        cartActions.Controls.Add(CommerceVisuals.Button("POS: invoice now", (_, _) => Run(CreatePosSale)));
        cartActions.Controls.Add(CommerceVisuals.Button("Remove selected line", (_, _) => RemoveCartLine()));
        cartPanel.Controls.Add(_cartGrid);
        cartPanel.Controls.Add(cartActions);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44 };
        actions.Controls.Add(CommerceVisuals.Button("Invoice selected order", (_, _) => Run(InvoiceOrder)));
        root.Controls.Add(composer, 0, 0);
        root.Controls.Add(cartPanel, 0, 1);
        root.Controls.Add(_ordersGrid, 0, 2);
        page.Controls.Add(root);
        page.Controls.Add(actions);
        _cartGrid.DataSource = _cart;
        _product.SelectedIndexChanged += (_, _) => UpdateVariants();
        _variant.SelectedIndexChanged += (_, _) => UpdatePriceForVariant();
        return page;
    }

    private TabPage BuildInvoicesTab()
    {
        var page = new TabPage("Invoices, payments & returns");
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal, SplitterDistance = 310 };
        split.Panel1.Controls.Add(_invoicesGrid);
        split.Panel2.Controls.Add(_invoiceLinesGrid);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44 };
        actions.Controls.Add(CommerceVisuals.Button("Record customer payment", (_, _) => Run(RecordPayment)));
        actions.Controls.Add(CommerceVisuals.Button("Return selected line", (_, _) => Run(ReturnLine)));
        actions.Controls.Add(CommerceVisuals.Button("Print invoice / receipt", (_, _) => Run(PrintSelectedInvoice)));
        page.Controls.Add(split);
        page.Controls.Add(actions);
        _invoicesGrid.SelectionChanged += (_, _) => LoadSalesInvoiceLines();
        return page;
    }

    private TabPage BuildRatesTab()
    {
        var page = new TabPage("Tax & discounts");
        var panel = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(16) };
        panel.Controls.Add(new Label { Text = "Tax rates and discounts are percentage-based and can be reused on each sale.", AutoSize = true, Margin = new Padding(5, 12, 12, 12) });
        panel.Controls.Add(CommerceVisuals.Button("Add tax rate", (_, _) => Run(() => EditRate(false))));
        panel.Controls.Add(CommerceVisuals.Button("Add discount", (_, _) => Run(() => EditRate(true))));
        page.Controls.Add(_ratesGrid);
        page.Controls.Add(panel);
        return page;
    }

    private void RefreshAll()
    {
        _customersGrid.DataSource = _commerce.GetCustomers();
        _ordersGrid.DataSource = _commerce.GetSalesOrders();
        _invoicesGrid.DataSource = _commerce.GetSalesInvoices();
        _ratesGrid.DataSource = _commerce.GetTaxRates()
            .Select(rate => new RateDisplay("Tax", rate.Name, rate.Percent))
            .Concat(_commerce.GetDiscounts().Select(rate => new RateDisplay("Discount", rate.Name, rate.Percent)))
            .ToList();
        Bind(_customer, _commerce.GetCustomers());
        Bind(_warehouse, _inventory.Warehouses.GetAll());
        Bind(_product, _inventory.Products.GetAll());
        Bind(_taxRate, _commerce.GetTaxRates());
        Bind(_discount, _commerce.GetDiscounts());
        LoadCustomerHistory();
        LoadSalesInvoiceLines();
    }

    private void EditCustomer(Customer? selected)
    {
        var values = CommerceDialogs.Prompt(FindForm(), selected == null ? "Add customer" : "Edit customer",
            ("Name", selected?.Name ?? ""), ("Phone", selected?.Phone ?? ""), ("Email", selected?.Email ?? ""),
            ("Address", selected?.Address ?? ""), ("Tax number", selected?.TaxNumber ?? ""));
        if (values == null) return;
        Run(() =>
        {
            _commerce.SaveCustomer(new Customer
            {
                Id = selected?.Id ?? 0, Name = values[0], Phone = values[1], Email = values[2],
                Address = values[3], TaxNumber = values[4]
            });
            RefreshAll();
        });
    }

    private void CreateOrder()
    {
        var customer = _customer.SelectedItem as Customer ?? throw new InvalidOperationException("Select a customer.");
        var warehouse = _warehouse.SelectedItem as InventoryManager.Models.Warehouse ?? throw new InvalidOperationException("Select a warehouse.");
        if (_cart.Count == 0) throw new InvalidOperationException("Add at least one product line.");
        _commerce.CreateSalesOrder(customer.Id, warehouse.Id, _cart.ToList(), "");
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
            UnitPrice = _unitPrice.Value
        });
    }

    private void RemoveCartLine()
    {
        if (_cartGrid.CurrentRow?.DataBoundItem is CommerceOrderLineInput line)
            _cart.Remove(line);
    }

    private void CreatePosSale()
    {
        var customer = _customer.SelectedItem as Customer ?? throw new InvalidOperationException("Select a customer.");
        var warehouse = _warehouse.SelectedItem as InventoryManager.Models.Warehouse ?? throw new InvalidOperationException("Select a warehouse.");
        if (_cart.Count == 0) throw new InvalidOperationException("Add at least one product line.");
        _commerce.CreatePosSale(customer.Id, warehouse.Id, _cart.ToList(), (_discount.SelectedItem as TaxRate)?.Percent ?? 0,
            (_taxRate.SelectedItem as TaxRate)?.Percent ?? 0, _auth.CurrentUser?.Id ?? 0, "");
        _cart.Clear();
        RefreshAll();
        _tabs.SelectedIndex = 3;
    }

    private void InvoiceOrder()
    {
        var order = Selected<SalesOrderSummary>(_ordersGrid) ?? throw new InvalidOperationException("Select a sales order.");
        _commerce.InvoiceSalesOrder(order.Id, (_discount.SelectedItem as TaxRate)?.Percent ?? 0,
            (_taxRate.SelectedItem as TaxRate)?.Percent ?? 0, _auth.CurrentUser?.Id ?? 0);
        RefreshAll();
        _tabs.SelectedIndex = 3;
    }

    private void RecordPayment()
    {
        var invoice = Selected<SalesInvoiceSummary>(_invoicesGrid) ?? throw new InvalidOperationException("Select an invoice.");
        var values = CommerceDialogs.Prompt(FindForm(), $"Payment for {invoice.Number}",
            ("Amount", invoice.BalanceDue.ToString("0.00")), ("Method", "Cash"), ("Reference", ""), ("Notes", ""));
        if (values == null) return;
        if (!decimal.TryParse(values[0], out var amount)) throw new FormatException("Enter a valid payment amount.");
        _commerce.RecordCustomerPayment(invoice.Id, amount, values[1], values[2], values[3]);
        RefreshAll();
    }

    private void ReturnLine()
    {
        var invoice = Selected<SalesInvoiceSummary>(_invoicesGrid) ?? throw new InvalidOperationException("Select an invoice.");
        var line = Selected<CommerceLine>(_invoiceLinesGrid) ?? throw new InvalidOperationException("Select an invoice line.");
        var values = CommerceDialogs.Prompt(FindForm(), $"Return {line.ProductName}", ("Quantity", "1"), ("Reason", ""));
        if (values == null) return;
        if (!int.TryParse(values[0], out var quantity)) throw new FormatException("Enter a valid return quantity.");
        _commerce.ReturnSalesInvoice(invoice.Id, line.Id, quantity, _auth.CurrentUser?.Id ?? 0, values[1]);
        RefreshAll();
    }

    private void PrintSelectedInvoice()
    {
        var invoice = Selected<SalesInvoiceSummary>(_invoicesGrid)
            ?? throw new InvalidOperationException("Select a sales invoice.");
        var lines = _commerce.GetSalesInvoiceLines(invoice.Id);
        InvoicePrintService.PreviewSalesInvoice(FindForm(), invoice, lines);
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

    private void UpdateVariants()
    {
        var product = _product.SelectedItem as Product;
        var variants = product == null ? new List<ProductVariant>() : _inventory.Products.GetVariants(product.Id).Where(item => item.IsActive).ToList();
        Bind(_variant, variants);
        if (product != null) _unitPrice.Value = Math.Clamp(product.SellPrice, _unitPrice.Minimum, _unitPrice.Maximum);
        UpdatePriceForVariant();
    }

    private void UpdatePriceForVariant()
    {
        var product = _product.SelectedItem as Product;
        var variant = _variant.SelectedItem as ProductVariant;
        if (product != null && variant != null)
            _unitPrice.Value = Math.Clamp(product.SellPrice + variant.PriceDelta, _unitPrice.Minimum, _unitPrice.Maximum);
    }

    private void LoadCustomerHistory()
    {
        var customer = Selected<Customer>(_customersGrid);
        _customerHistoryGrid.DataSource = customer == null ? null : _commerce.GetCustomerHistory(customer.Id);
    }

    private void LoadSalesInvoiceLines()
    {
        var invoice = Selected<SalesInvoiceSummary>(_invoicesGrid);
        _invoiceLinesGrid.DataSource = invoice == null ? null : _commerce.GetSalesInvoiceLines(invoice.Id);
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception exception) { CommerceDialogs.ShowError(FindForm(), exception); }
    }

    private static T? Selected<T>(DataGridView grid) where T : class =>
        grid.CurrentRow?.DataBoundItem as T;

    private static ComboBox Dropdown() => new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 145 };

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
