using InventoryManager.Services;

namespace InventoryManager.Forms;

public class TransactionHistoryPage : UserControl
{
    private readonly InventoryService _inventory;
    private readonly DataGridView _grid = new();
    private readonly DateTimePicker _from = new();
    private readonly DateTimePicker _to = new();

    public TransactionHistoryPage(InventoryService inventory)
    {
        _inventory = inventory;
        Dock = DockStyle.Fill;
        BackColor = Color.White;

        var filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 46, Padding = new Padding(8), WrapContents = false };
        _from.Format = DateTimePickerFormat.Short;
        _from.Value = DateTime.Today.AddMonths(-1);
        _to.Format = DateTimePickerFormat.Short;
        _to.Value = DateTime.Today;
        var refresh = new Button { Text = "Search", AutoSize = true };
        refresh.Click += (_, _) => LoadTransactions();
        filters.Controls.AddRange(new Control[]
        {
            new Label { Text = "From:", AutoSize = true, Padding = new Padding(2, 6, 0, 0) }, _from,
            new Label { Text = "To:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) }, _to, refresh
        });
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowHeadersVisible = false;
        Controls.Add(_grid);
        Controls.Add(filters);
        LoadTransactions();
    }

    private void LoadTransactions()
    {
        var untilExclusive = _to.Value.Date.AddDays(1);
        _grid.DataSource = _inventory.Transactions.GetAll(_from.Value.Date, untilExclusive);
        foreach (var name in new[] { "Id", "ProductId", "VariantId", "WarehouseId", "BatchId", "UserId" })
            if (_grid.Columns.Contains(name)) _grid.Columns[name].Visible = false;
        if (_grid.Columns.Contains("Date")) _grid.Columns["Date"].DefaultCellStyle.Format = "g";
    }
}
