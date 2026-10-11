using InventoryManager.Services;

namespace InventoryManager.Forms;

public class AlertsPage : UserControl
{
    private readonly InventoryService _inventory;
    private readonly DataGridView _grid = new();
    private readonly NumericUpDown _expiryDays = new();

    public AlertsPage(InventoryService inventory)
    {
        _inventory = inventory;
        Dock = DockStyle.Fill;
        BackColor = Color.White;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8), WrapContents = false };
        _expiryDays.Minimum = 0;
        _expiryDays.Maximum = 3650;
        _expiryDays.Value = 30;
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        refresh.Click += (_, _) => LoadAlerts();
        top.Controls.AddRange(new Control[]
        {
            new Label { Text = "Expiry window (days):", AutoSize = true, Padding = new Padding(0, 6, 0, 0) },
            _expiryDays, refresh
        });
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowHeadersVisible = false;
        Controls.Add(_grid);
        Controls.Add(top);
        LoadAlerts();
    }

    private void LoadAlerts()
    {
        var alerts = _inventory.Alerts.GetAlerts((int)_expiryDays.Value);
        _grid.DataSource = alerts;
        foreach (var name in new[] { "ProductId", "BatchId" })
            if (_grid.Columns.Contains(name)) _grid.Columns[name].Visible = false;
        if (_grid.Columns.Contains("Severity")) _grid.Columns["Severity"].DisplayIndex = 0;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is not Models.StockAlert alert) continue;
            row.DefaultCellStyle.BackColor = alert.Severity == "Critical"
                ? Color.FromArgb(255, 220, 220)
                : Color.FromArgb(255, 245, 210);
        }
    }
}
