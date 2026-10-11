using InventoryManager.Models;
using InventoryManager.Services;

namespace InventoryManager.Forms;

public sealed class ReportsPage : UserControl
{
    private readonly InventoryService _inventory;
    private readonly CommerceService _commerce;
    private readonly DataGridView _grid = CommerceVisuals.Grid();
    private readonly BarChartControl _chart = new() { Dock = DockStyle.Fill };
    private readonly ComboBox _reportType = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 190 };
    private readonly DateTimePicker _from = new() { Format = DateTimePickerFormat.Short, Width = 115 };
    private readonly DateTimePicker _to = new() { Format = DateTimePickerFormat.Short, Width = 115 };
    private readonly TableLayoutPanel _cards = new() { Dock = DockStyle.Fill, ColumnCount = 4 };
    private IReadOnlyList<CommerceReportRow> _rows = Array.Empty<CommerceReportRow>();

    public ReportsPage(InventoryService inventory, CommerceService commerce)
    {
        _inventory = inventory;
        _commerce = commerce;
        Dock = DockStyle.Fill;
        BackColor = CommerceVisuals.Canvas;
        _from.Value = DateTime.Today.AddDays(-30);
        _to.Value = DateTime.Today;
        _reportType.Items.AddRange(new object[]
        {
            "Sales", "Purchases", "Profit and loss", "Stock valuation", "Fast movers", "Slow movers"
        });
        _reportType.SelectedIndex = 0;
        BuildLayout();
        RefreshReport();
    }

    private void BuildLayout()
    {
        for (var i = 0; i < 4; i++) _cards.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            WrapContents = false,
            Padding = new Padding(12, 8, 12, 8)
        };
        toolbar.Controls.Add(new Label { Text = "Report", AutoSize = true, Margin = new Padding(4, 10, 2, 0) });
        toolbar.Controls.Add(_reportType);
        toolbar.Controls.Add(new Label { Text = "From", AutoSize = true, Margin = new Padding(8, 10, 2, 0) });
        toolbar.Controls.Add(_from);
        toolbar.Controls.Add(new Label { Text = "To", AutoSize = true, Margin = new Padding(8, 10, 2, 0) });
        toolbar.Controls.Add(_to);
        toolbar.Controls.Add(CommerceVisuals.Button("Refresh", (_, _) => Run(RefreshReport)));
        toolbar.Controls.Add(CommerceVisuals.Button("Export Excel", (_, _) => Export(true)));
        toolbar.Controls.Add(CommerceVisuals.Button("Export PDF", (_, _) => Export(false)));

        var split = new SplitContainer
        {
            Size = new Size(800, 400),
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            Panel1MinSize = 0,
            Panel2MinSize = 0,
            SplitterDistance = 350
        };
        split.Panel1.Controls.Add(_chart);
        split.Panel2.Controls.Add(_grid);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(8) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(toolbar, 0, 0);
        layout.Controls.Add(_cards, 0, 1);
        layout.Controls.Add(split, 0, 2);
        Controls.Add(layout);
    }

    private void RefreshReport()
    {
        var from = _from.Value.Date;
        var to = _to.Value.Date.AddDays(1);
        var selected = Convert.ToString(_reportType.SelectedItem);
        switch (selected)
        {
            case "Sales":
                _rows = _commerce.GetSalesReport(from, to);
                break;
            case "Purchases":
                _rows = _commerce.GetPurchaseReport(from, to);
                break;
            case "Profit and loss":
                var result = _commerce.GetProfitLoss(from, to);
                _rows = new[]
                {
                    new CommerceReportRow { Date = from, Reference = "Period total", Revenue = result.revenue, Cost = result.cost }
                };
                break;
            case "Stock valuation":
                _rows = _inventory.Products.GetAll().Select(product => new CommerceReportRow
                {
                    Product = product.Name,
                    Quantity = product.TotalQuantity,
                    Cost = product.CostPrice * product.TotalQuantity,
                    Revenue = product.CostPrice * product.TotalQuantity
                }).OrderByDescending(row => row.Revenue).ToList();
                break;
            case "Fast movers":
                _rows = _commerce.GetProductMovementReport(from, to)
                    .OrderByDescending(row => row.Quantity).Take(25).ToList();
                break;
            case "Slow movers":
                _rows = _commerce.GetProductMovementReport(from, to)
                    .OrderBy(row => row.Quantity).Take(25).ToList();
                break;
            default:
                throw new InvalidOperationException("Select a report type.");
        }

        _grid.DataSource = _rows.ToList();
        var title = selected ?? "Report";
        _cards.Controls.Clear();
        _cards.Controls.Add(CommerceVisuals.Card("Records", _rows.Count.ToString(), CommerceVisuals.Accent), 0, 0);
        _cards.Controls.Add(CommerceVisuals.Card("Revenue / valuation", _rows.Sum(row => row.Revenue).ToString("C"), Color.FromArgb(0, 150, 136)), 1, 0);
        _cards.Controls.Add(CommerceVisuals.Card("Cost", _rows.Sum(row => row.Cost).ToString("C"), Color.FromArgb(226, 145, 33)), 2, 0);
        _cards.Controls.Add(CommerceVisuals.Card("Profit", _rows.Sum(row => row.Profit).ToString("C"), Color.FromArgb(112, 92, 190)), 3, 0);
        var chartData = _rows.GroupBy(row => string.IsNullOrWhiteSpace(row.Product)
                ? row.Date == default ? row.Reference : row.Date.ToString("MMM dd")
                : row.Product)
            .Select(group => (group.Key, group.Sum(row => row.Revenue)))
            .OrderByDescending(pair => Math.Abs(pair.Item2)).Take(10);
        _chart.SetData(chartData);
        Text = title;
    }

    private void Export(bool excel)
    {
        if (_rows.Count == 0)
        {
            MessageBox.Show(FindForm(), "There are no rows to export.", "Export report", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Filter = excel ? "Excel XML workbook (*.xml)|*.xml" : "PDF document (*.pdf)|*.pdf",
            FileName = $"{Convert.ToString(_reportType.SelectedItem)?.Replace(' ', '-')}-{DateTime.Today:yyyyMMdd}.{(excel ? "xml" : "pdf")}",
            AddExtension = true,
            DefaultExt = excel ? "xml" : "pdf"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        Run(() =>
        {
            if (excel) CommerceReportExportService.ExportExcel(dialog.FileName, Convert.ToString(_reportType.SelectedItem) ?? "Report", _rows);
            else CommerceReportExportService.ExportPdf(dialog.FileName, Convert.ToString(_reportType.SelectedItem) ?? "Report", _rows);
            MessageBox.Show(FindForm(), "Report exported successfully.", "Export report", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    private void Run(Action action)
    {
        try { action(); }
        catch (Exception exception) { CommerceDialogs.ShowError(FindForm(), exception); }
    }
}
