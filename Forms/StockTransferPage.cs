using InventoryManager.Models;
using InventoryManager.Services;

namespace InventoryManager.Forms;

public class StockTransferPage : UserControl
{
    private readonly InventoryService _inventory;
    private readonly AuthService _auth;
    private readonly ComboBox _product = new();
    private readonly ComboBox _variant = new();
    private readonly ComboBox _batch = new();
    private readonly ComboBox _from = new();
    private readonly ComboBox _to = new();
    private readonly NumericUpDown _quantity = new();
    private readonly TextBox _notes = new();

    public StockTransferPage(InventoryService inventory, AuthService auth)
    {
        _inventory = inventory;
        _auth = auth;
        Dock = DockStyle.Fill;
        BackColor = Color.White;

        _product.DropDownStyle = ComboBoxStyle.DropDownList;
        _product.DataSource = inventory.Products.GetAll().Where(p => p.IsActive).ToList();
        _product.DisplayMember = nameof(Product.Name);
        _product.SelectedIndexChanged += (_, _) => { LoadVariants(); LoadBatches(); };
        _variant.DropDownStyle = ComboBoxStyle.DropDownList;
        BindWarehouses(_from);
        BindWarehouses(_to);
        _from.SelectedIndexChanged += (_, _) => LoadBatches();
        _batch.DropDownStyle = ComboBoxStyle.DropDownList;
        _batch.DisplayMember = nameof(StockBatch.BatchNumber);
        var defaultWarehouse = inventory.Warehouses.GetDefault();
        if (defaultWarehouse != null) _from.SelectedValue = defaultWarehouse.Id;
        var destination = inventory.Warehouses.GetAll().FirstOrDefault(w => w.Id != defaultWarehouse?.Id);
        if (destination != null) _to.SelectedValue = destination.Id;
        _quantity.Minimum = 1;
        _quantity.Maximum = int.MaxValue;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 7 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(layout, 0, "Product:", _product);
        AddRow(layout, 1, "Variant:", _variant);
        AddRow(layout, 2, "From:", _from);
        AddRow(layout, 3, "To:", _to);
        AddRow(layout, 4, "Batch / lot:", _batch);
        AddRow(layout, 5, "Quantity:", _quantity);
        AddRow(layout, 6, "Notes:", _notes);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        var transfer = new Button { Text = "Transfer stock", Width = 125, Height = 30 };
        var cancel = new Button { Text = "Clear notes", Width = 90, Height = 30 };
        transfer.Click += (_, _) => Transfer();
        cancel.Click += (_, _) => _notes.Clear();
        buttons.Controls.AddRange(new Control[] { transfer, cancel });
        Controls.Add(layout);
        Controls.Add(buttons);
        LoadVariants();
    }

    private void BindWarehouses(ComboBox combo)
    {
        combo.DropDownStyle = ComboBoxStyle.DropDownList;
        combo.DataSource = _inventory.Warehouses.GetAll();
        combo.DisplayMember = nameof(Warehouse.Name);
        combo.ValueMember = nameof(Warehouse.Id);
    }

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
        var warehouseId = _from.SelectedValue is int id ? id : 0;
        var batches = new List<StockBatch> { new() { Id = 0, BatchNumber = "(No batch selected)" } };
        if (product != null && warehouseId > 0)
            batches.AddRange(_inventory.Batches.GetForProduct(product.Id, warehouseId).Where(b => b.Quantity > 0));
        _batch.DataSource = batches;
    }

    private void Transfer()
    {
        if (_product.SelectedItem is not Product product || _from.SelectedValue is not int fromId ||
            _to.SelectedValue is not int toId)
        {
            MessageBox.Show(FindForm(), "Select a product and both locations.", "Missing information",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (fromId == toId)
        {
            MessageBox.Show(FindForm(), "Choose different source and destination locations.", "Invalid transfer",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
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
            MessageBox.Show(FindForm(), "Select a batch before transferring tracked stock.", "Batch required",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            _inventory.StockService.Transfer(product.Id, variantId is > 0 ? variantId : null, fromId, toId,
                (int)_quantity.Value, _auth.CurrentUser?.Id ?? 0, _notes.Text.Trim(), batch);
            MessageBox.Show(FindForm(), "Transfer completed.", "Stock transferred", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _notes.Clear();
        }
        catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Transfer failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
