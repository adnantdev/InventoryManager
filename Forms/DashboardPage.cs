using InventoryManager.Services;

namespace InventoryManager.Forms;

public class DashboardPage : UserControl
{
    public DashboardPage(InventoryService inventory)
    {
        Dock = DockStyle.Fill;
        BackColor = CommerceVisuals.Canvas;
        Padding = new Padding(12);

        var (total, low, outOfStock, value, todayTransactions) = inventory.GetDashboardStats();
        var products = inventory.Products.GetAll();
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(CommerceVisuals.Heading("Inventory overview"), 0, 0);

        var cards = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 5 };
        for (var index = 0; index < 5; index++) cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20));
        cards.Controls.Add(CommerceVisuals.Card("Products", total.ToString(), CommerceVisuals.Accent), 0, 0);
        cards.Controls.Add(CommerceVisuals.Card("Low stock", low.ToString(), Color.FromArgb(226, 145, 33)), 1, 0);
        cards.Controls.Add(CommerceVisuals.Card("Out of stock", outOfStock.ToString(), Color.FromArgb(201, 78, 78)), 2, 0);
        cards.Controls.Add(CommerceVisuals.Card("Inventory value", value.ToString("C"), Color.FromArgb(0, 150, 136)), 3, 0);
        cards.Controls.Add(CommerceVisuals.Card("Today's movements", todayTransactions.ToString(), Color.FromArgb(112, 92, 190)), 4, 0);
        root.Controls.Add(cards, 0, 1);

        var bottom = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 8, 0, 0) };
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        var stockPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(12), BackColor = Color.White };
        var chart = new BarChartControl { Dock = DockStyle.Fill };
        chart.SetData(products.OrderBy(product => product.TotalQuantity).Take(10)
            .Select(product => (product.Name, (decimal)product.TotalQuantity)));
        stockPanel.Controls.Add(chart);

        var modulesPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(18), BackColor = Color.White };
        var modules = new Label
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.TopLeft,
            Font = new Font("Segoe UI", 10),
            ForeColor = CommerceVisuals.Ink,
            Text = "Inventory control\n\n" +
                   "Products, SKUs, variants and barcodes\n" +
                   "Categories, subcategories and units of measure\n" +
                   "Multiple warehouses, stock transfers and adjustments\n" +
                   "Batch / lot and expiry tracking\n" +
                   "Low-stock and out-of-stock alerts\n\n" +
                   "Business operations\n\n" +
                   "Purchasing, receiving, supplier invoices and payments\n" +
                   "Sales, POS invoices, customer payments and returns\n" +
                   "Tax / discount rates, stock valuation and reports"
        };
        modulesPanel.Controls.Add(modules);
        bottom.Controls.Add(stockPanel, 0, 0);
        bottom.Controls.Add(modulesPanel, 1, 0);
        root.Controls.Add(bottom, 0, 2);
        Controls.Add(root);
    }
}
