using InventoryManager.Models;
using InventoryManager.Services;

namespace InventoryManager.Forms;

public class UnitPage : UserControl
{
    private readonly InventoryService _inventory;
    private readonly DataGridView _grid = new();

    public UnitPage(InventoryService inventory)
    {
        _inventory = inventory;
        Dock = DockStyle.Fill;
        BackColor = Color.White;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
        var add = new Button { Text = "Add unit", AutoSize = true };
        var edit = new Button { Text = "Edit", AutoSize = true };
        var delete = new Button { Text = "Delete", AutoSize = true };
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        add.Click += (_, _) => EditUnit(null);
        edit.Click += (_, _) => { if (SelectedUnit() is { } unit) EditUnit(unit); };
        delete.Click += (_, _) => DeleteUnit();
        refresh.Click += (_, _) => LoadUnits();
        buttons.Controls.AddRange(new Control[] { add, edit, delete, refresh });
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowHeadersVisible = false;
        Controls.Add(_grid);
        Controls.Add(buttons);
        LoadUnits();
    }

    private UnitOfMeasure? SelectedUnit() => _grid.CurrentRow?.DataBoundItem as UnitOfMeasure;

    private void LoadUnits()
    {
        _grid.DataSource = _inventory.Units.GetAll();
        if (_grid.Columns.Contains("Id")) _grid.Columns["Id"].Visible = false;
    }

    private void EditUnit(UnitOfMeasure? existing)
    {
        using var dialog = new Form
        {
            Text = existing == null ? "Add unit" : "Edit unit",
            Width = 390,
            Height = 255,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false
        };
        var code = new TextBox { Left = 145, Top = 20, Width = 200, Text = existing?.Code ?? "" };
        var name = new TextBox { Left = 145, Top = 55, Width = 200, Text = existing?.Name ?? "" };
        var factor = new NumericUpDown
        {
            Left = 145, Top = 90, Width = 200, DecimalPlaces = 4, Minimum = 0.0001m,
            Maximum = 100000000m, Value = existing?.ConversionFactor > 0 ? existing.ConversionFactor : 1
        };
        dialog.Controls.AddRange(new Control[]
        {
            new Label { Text = "Code:", Left = 25, Top = 23, AutoSize = true }, code,
            new Label { Text = "Name:", Left = 25, Top = 58, AutoSize = true }, name,
            new Label { Text = "Conversion factor:", Left = 25, Top = 93, AutoSize = true }, factor
        });
        var save = new Button { Text = "Save", Left = 145, Top = 145, Width = 90, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 245, Top = 145, Width = 90, DialogResult = DialogResult.Cancel };
        dialog.Controls.AddRange(new Control[] { save, cancel });
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        if (string.IsNullOrWhiteSpace(code.Text) || string.IsNullOrWhiteSpace(name.Text))
        {
            MessageBox.Show(FindForm(), "Code and name are required.", "Invalid unit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var unit = existing ?? new UnitOfMeasure();
        unit.Code = code.Text.Trim();
        unit.Name = name.Text.Trim();
        unit.ConversionFactor = factor.Value;
        try
        {
            if (existing == null) _inventory.Units.Add(unit);
            else _inventory.Units.Update(unit);
            LoadUnits();
        }
        catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Unit error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void DeleteUnit()
    {
        if (SelectedUnit() is not { } unit) return;
        if (MessageBox.Show(FindForm(), $"Delete unit '{unit.Code}'?", "Confirm", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try { _inventory.Units.Delete(unit.Id); LoadUnits(); }
        catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Unit error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
