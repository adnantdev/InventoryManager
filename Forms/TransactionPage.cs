using InventoryManager.Models;
using InventoryManager.Services;

namespace InventoryManager.Forms;

public class TransactionPage : UserControl
{
    private readonly InventoryService _inventory;
    private readonly AuthService _auth;
    private readonly ComboBox _product = new();
    private readonly ComboBox _variant = new();
    private readonly ComboBox _warehouse = new();
    private readonly ComboBox _batch = new();
    private readonly ComboBox _type = new();
    private readonly NumericUpDown _quantity = new();
    private readonly TextBox _reference = new();
    private readonly TextBox _notes = new();

    public TransactionPage(InventoryService inventory, AuthService auth)
    {
        _inventory = inventory;
        _auth = auth;
        Dock = DockStyle.Fill;
        BackColor = Color.White;

        _product.DropDownStyle = ComboBoxStyle.DropDownList;
        _product.DataSource = _inventory.Products.GetAll().Where(p => p.IsActive).ToList();
        _product.DisplayMember = nameof(Product.Name);
        _product.SelectedIndexChanged += (_, _) => { LoadVariants(); LoadBatches(); };
        _variant.DropDownStyle = ComboBoxStyle.DropDownList;
        _warehouse.DropDownStyle = ComboBoxStyle.DropDownList;
        _warehouse.DataSource = _inventory.Warehouses.GetAll();
        _warehouse.DisplayMember = nameof(Warehouse.Name);
        _warehouse.ValueMember = nameof(Warehouse.Id);
        _warehouse.SelectedIndexChanged += (_, _) => LoadBatches();
        var defaultWarehouse = _inventory.Warehouses.GetDefault();
        if (defaultWarehouse != null) _warehouse.SelectedValue = defaultWarehouse.Id;
        _type.DropDownStyle = ComboBoxStyle.DropDownList;
        _type.DataSource = new[] { TransactionType.StockIn, TransactionType.StockOut, TransactionType.Sale, TransactionType.Return };
        _type.SelectedIndexChanged += (_, _) => LoadBatches();
        _batch.DropDownStyle = ComboBoxStyle.DropDownList;
        _batch.DisplayMember = nameof(StockBatch.BatchNumber);
        _quantity.Minimum = 1;
        _quantity.Maximum = int.MaxValue;

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), ColumnCount = 2, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 115));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        AddRow(layout, 0, "Product:", _product);
        AddRow(layout, 1, "Variant:", _variant);
        AddRow(layout, 2, "Warehouse:", _warehouse);
        AddRow(layout, 3, "Movement:", _type);
        AddRow(layout, 4, "Batch / lot:", _batch);
        AddRow(layout, 5, "Quantity:", _quantity);
        AddRow(layout, 6, "Reference:", _reference);
        AddRow(layout, 7, "Notes:", _notes);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 55, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10) };
        var save = new Button { Text = "Post movement", Width = 130, Height = 30 };
        var cancel = new Button { Text = "Clear", Width = 90, Height = 30 };
        save.Click += (_, _) => PostMovement();
        cancel.Click += (_, _) => { _reference.Clear(); _notes.Clear(); };
        buttons.Controls.AddRange(new Control[] { save, cancel });
        Controls.Add(layout);
        Controls.Add(buttons);
        LoadVariants();
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
        var product = _product.SelectedItem as Product;
        var variants = new List<ProductVariant> { new() { Id = 0, Attributes = "(No variant)", SKU = "" } };
        if (product != null) variants.AddRange(_inventory.Products.GetVariants(product.Id).Where(v => v.IsActive));
        _variant.DataSource = variants;
    }

    private void LoadBatches()
    {
        var product = _product.SelectedItem as Product;
        var warehouseId = _warehouse.SelectedValue is int id ? id : 0;
        var batches = new List<StockBatch> { new() { Id = 0, BatchNumber = "(No batch selected)" } };
        if (product != null && warehouseId > 0)
            batches.AddRange(_inventory.Batches.GetForProduct(product.Id, warehouseId).Where(b => b.Quantity > 0));
        _batch.DataSource = batches;
    }

    private void PostMovement()
    {
        if (_product.SelectedItem is not Product product || _warehouse.SelectedValue is not int warehouseId ||
            _type.SelectedItem is not TransactionType type)
        {
            MessageBox.Show(FindForm(), "Select a product, warehouse, and movement type.", "Missing information",
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
            MessageBox.Show(FindForm(),
                type is TransactionType.StockIn or TransactionType.Return
                    ? "Receive tracked stock from the Batches / Lots screen so batch and stock quantities stay aligned."
                    : "Select a batch for this outgoing movement.",
                "Batch required", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        try
        {
            _inventory.StockService.Move(product.Id, variantId is > 0 ? variantId : null, warehouseId, type,
                (int)_quantity.Value, _auth.CurrentUser?.Id ?? 0, _reference.Text.Trim(), _notes.Text.Trim(), batch);
            MessageBox.Show(FindForm(), "Stock movement was recorded.", "Stock updated", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _reference.Clear();
            _notes.Clear();
        }
        catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Stock movement failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
