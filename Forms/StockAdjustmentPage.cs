using InventoryManager.Models;
using InventoryManager.Services;

namespace InventoryManager.Forms;

public class StockAdjustmentPage : UserControl
{
    private readonly InventoryService _inventory;
    private readonly AuthService _auth;
    private readonly ComboBox _product = new();
    private readonly ComboBox _variant = new();
    private readonly ComboBox _warehouse = new();
    private readonly ComboBox _batch = new();
    private readonly ComboBox _reason = new();
    private readonly NumericUpDown _newQuantity = new();
    private readonly TextBox _notes = new();

    public StockAdjustmentPage(InventoryService inventory, AuthService auth)
    {
        _inventory = inventory;
        _auth = auth;
        Dock = DockStyle.Fill;
        BackColor = Color.White;

        _product.DropDownStyle = ComboBoxStyle.DropDownList;
        _product.DataSource = inventory.Products.GetAll().Where(p => p.IsActive).ToList();
        _product.DisplayMember = nameof(Product.Name);
        _product.SelectedIndexChanged += (_, _) => { LoadVariants(); LoadBatches(); UpdateCurrentQuantity(); };
        _variant.DropDownStyle = ComboBoxStyle.DropDownList;
        _variant.SelectedIndexChanged += (_, _) => UpdateCurrentQuantity();
        _warehouse.DropDownStyle = ComboBoxStyle.DropDownList;
        _warehouse.DataSource = inventory.Warehouses.GetAll();
        _warehouse.DisplayMember = nameof(Warehouse.Name);
        _warehouse.ValueMember = nameof(Warehouse.Id);
        _warehouse.SelectedIndexChanged += (_, _) => { LoadBatches(); UpdateCurrentQuantity(); };
        _batch.DropDownStyle = ComboBoxStyle.DropDownList;
        _batch.DisplayMember = nameof(StockBatch.BatchNumber);
        _batch.SelectedIndexChanged += (_, _) => UpdateCurrentQuantity();
        _reason.DropDownStyle = ComboBoxStyle.DropDownList;
        _reason.DataSource = Enum.GetValues<AdjustmentReason>();
        _newQuantity.Minimum = 0;
        _newQuantity.Maximum = int.MaxValue;
        var current = new Label { AutoSize = true, Text = "Current quantity: —", Padding = new Padding(4) };

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 130));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(layout, 0, "Product:", _product);
        AddRow(layout, 1, "Variant:", _variant);
        AddRow(layout, 2, "Warehouse:", _warehouse);
        AddRow(layout, 3, "Batch / lot:", _batch);
        AddRow(layout, 4, "Current:", current);
        AddRow(layout, 5, "New quantity:", _newQuantity);
        AddRow(layout, 6, "Reason:", _reason);
        AddRow(layout, 7, "Notes:", _notes);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        var save = new Button { Text = "Apply adjustment", Width = 135, Height = 30 };
        var cancel = new Button { Text = "Clear notes", Width = 90, Height = 30 };
        save.Click += (_, _) => ApplyAdjustment();
        cancel.Click += (_, _) => _notes.Clear();
        buttons.Controls.AddRange(new Control[] { save, cancel });
        Controls.Add(layout);
        Controls.Add(buttons);
        _currentQuantity = current;
        LoadVariants();
        LoadBatches();
        UpdateCurrentQuantity();
    }

    private readonly Label _currentQuantity;

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control input)
    {
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        input.Dock = DockStyle.Fill;
        layout.Controls.Add(input, 1, row);
    }

    private void LoadVariants()
    {
        var variants = new List<ProductVariant> { new() { Id = 0, Attributes = "(No variant)" } };
        if (_product.SelectedItem is Product product)
            variants.AddRange(_inventory.Products.GetVariants(product.Id).Where(v => v.IsActive));
        _variant.DataSource = variants;
    }

    private void LoadBatches()
    {
        var product = _product.SelectedItem as Product;
        var warehouseId = _warehouse.SelectedValue is int id ? id : 0;
        var batches = new List<StockBatch> { new() { Id = 0, BatchNumber = "(No batch selected)" } };
        if (product != null && warehouseId > 0)
            batches.AddRange(_inventory.Batches.GetForProduct(product.Id, warehouseId).Where(b => b.Quantity >= 0));
        _batch.DataSource = batches;
    }

    private void UpdateCurrentQuantity()
    {
        if (_product.SelectedItem is not Product product || _warehouse.SelectedValue is not int warehouseId)
        {
            _currentQuantity.Text = "Current quantity: —";
            return;
        }
        var variantId = (_variant.SelectedItem as ProductVariant)?.Id;
        var batch = (_batch.SelectedItem as StockBatch) is { Id: > 0 } selectedBatch ? selectedBatch : null;
        var quantity = product.TrackBatches && batch != null
            ? batch.Quantity
            : _inventory.Stock.GetTotal(product.Id, variantId is > 0 ? variantId : null, warehouseId);
        _currentQuantity.Text = $"Current quantity: {quantity}";
        _newQuantity.Value = Math.Min(_newQuantity.Maximum, quantity);
    }

    private void ApplyAdjustment()
    {
        if (_product.SelectedItem is not Product product || _warehouse.SelectedValue is not int warehouseId ||
            _reason.SelectedItem is not AdjustmentReason reason) return;
        var variantId = (_variant.SelectedItem as ProductVariant)?.Id;
        if (product.HasVariants && variantId is not > 0)
        {
            MessageBox.Show(FindForm(), "Select a variant for this product.", "Variant required",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        var batch = (_batch.SelectedItem as StockBatch) is { Id: > 0 } selectedBatch ? selectedBatch : null;
        if (product.TrackBatches && batch == null)
        {
            MessageBox.Show(FindForm(), "Select a batch before adjusting tracked stock.", "Batch required",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            _inventory.StockService.Adjust(product.Id, variantId is > 0 ? variantId : null, warehouseId,
                (int)_newQuantity.Value, reason, _auth.CurrentUser?.Id ?? 0, _notes.Text.Trim(), batch?.Id);
            MessageBox.Show(FindForm(), "Stock adjustment recorded.", "Stock adjusted", MessageBoxButtons.OK, MessageBoxIcon.Information);
            UpdateCurrentQuantity();
            _notes.Clear();
        }
        catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Adjustment failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
