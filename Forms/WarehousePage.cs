using InventoryManager.Models;
using InventoryManager.Services;

namespace InventoryManager.Forms;

public class WarehousePage : UserControl
{
    private readonly InventoryService _inventory;
    private readonly DataGridView _grid = new();

    public WarehousePage(InventoryService inventory)
    {
        _inventory = inventory;
        Dock = DockStyle.Fill;
        BackColor = Color.White;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
        var add = new Button { Text = "Add location", AutoSize = true };
        var edit = new Button { Text = "Edit", AutoSize = true };
        var deactivate = new Button { Text = "Deactivate", AutoSize = true };
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        add.Click += (_, _) => EditWarehouse(null);
        edit.Click += (_, _) => { if (SelectedWarehouse() is { } warehouse) EditWarehouse(warehouse); };
        deactivate.Click += (_, _) => DeactivateWarehouse();
        refresh.Click += (_, _) => LoadWarehouses();
        buttons.Controls.AddRange(new Control[] { add, edit, deactivate, refresh });
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowHeadersVisible = false;
        Controls.Add(_grid);
        Controls.Add(buttons);
        LoadWarehouses();
    }

    private Warehouse? SelectedWarehouse() => _grid.CurrentRow?.DataBoundItem as Warehouse;

    private void LoadWarehouses()
    {
        _grid.DataSource = _inventory.Warehouses.GetAll(activeOnly: false);
        if (_grid.Columns.Contains("Id")) _grid.Columns["Id"].Visible = false;
    }

    private void EditWarehouse(Warehouse? existing)
    {
        using var dialog = new Form
        {
            Text = existing == null ? "Add location" : "Edit location",
            Width = 400,
            Height = 290,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false
        };
        var code = new TextBox { Left = 130, Top = 20, Width = 220, Text = existing?.Code ?? "" };
        var name = new TextBox { Left = 130, Top = 55, Width = 220, Text = existing?.Name ?? "" };
        var address = new TextBox { Left = 130, Top = 90, Width = 220, Text = existing?.Address ?? "" };
        var isDefault = new CheckBox { Left = 130, Top = 125, Width = 220, Text = "Default location", Checked = existing?.IsDefault ?? false };
        var active = new CheckBox { Left = 130, Top = 155, Width = 220, Text = "Active", Checked = existing?.IsActive ?? true };
        dialog.Controls.AddRange(new Control[]
        {
            new Label { Text = "Code:", Left = 25, Top = 23, AutoSize = true }, code,
            new Label { Text = "Name:", Left = 25, Top = 58, AutoSize = true }, name,
            new Label { Text = "Address:", Left = 25, Top = 93, AutoSize = true }, address,
            isDefault, active
        });
        var save = new Button { Text = "Save", Left = 130, Top = 195, Width = 95, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 235, Top = 195, Width = 95, DialogResult = DialogResult.Cancel };
        dialog.Controls.AddRange(new Control[] { save, cancel });
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        if (string.IsNullOrWhiteSpace(code.Text) || string.IsNullOrWhiteSpace(name.Text))
        {
            MessageBox.Show(FindForm(), "Code and name are required.", "Invalid location", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var warehouse = existing ?? new Warehouse();
        warehouse.Code = code.Text.Trim();
        warehouse.Name = name.Text.Trim();
        warehouse.Address = address.Text.Trim();
        warehouse.IsDefault = isDefault.Checked;
        warehouse.IsActive = active.Checked;
        try
        {
            if (existing == null) _inventory.Warehouses.Add(warehouse);
            else _inventory.Warehouses.Update(warehouse);
            LoadWarehouses();
        }
        catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Location error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void DeactivateWarehouse()
    {
        if (SelectedWarehouse() is not { } warehouse) return;
        if (MessageBox.Show(FindForm(), $"Deactivate '{warehouse.Name}'? Historical stock records remain.", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { _inventory.Warehouses.Delete(warehouse.Id); LoadWarehouses(); }
        catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Location error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
