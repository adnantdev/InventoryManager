using InventoryManager.Models;
using InventoryManager.Services;

namespace InventoryManager.Forms;

public class BatchEditDialog : Form
{
    private readonly InventoryService _inv;
    private readonly StockBatch _b;
    private readonly int _userId;

    private TextBox _batch = null!, _lot = null!, _qty = null!, _cost = null!, _supplier = null!;
    private DateTimePicker _manufacture = null!, _expiry = null!;
    private ComboBox _warehouse = null!, _variant = null!;
    private CheckBox _hasExpiry = null!;

    public BatchEditDialog(InventoryService inv, int productId, StockBatch? existing, int userId = 0)
    {
        _inv = inv;
        _userId = userId;
        _b = existing ?? new StockBatch { ProductId = productId };
        Text = existing == null ? "Add Batch" : "Edit Batch";
        Size = new Size(460, 470);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;

        int y = 20;
        _batch = AddField("Batch #:", ref y, _b.BatchNumber);
        _lot   = AddField("Lot #:",   ref y, _b.LotNumber);
        _qty   = AddField("Quantity:", ref y, _b.Quantity.ToString());
        _cost  = AddField("Cost:",     ref y, _b.CostPrice.ToString("0.##"));
        _supplier = AddField("Supplier:", ref y, _b.Supplier);

        Controls.Add(new Label { Text = "Warehouse:", Location = new Point(20, y + 3), AutoSize = true });
        _warehouse = new ComboBox { Location = new Point(140, y), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
        _warehouse.DataSource = inv.Warehouses.GetAll();
        _warehouse.DisplayMember = "Name";
        _warehouse.ValueMember = "Id";
        if (_b.WarehouseId > 0) _warehouse.SelectedValue = _b.WarehouseId;
        else
        {
            var def = inv.Warehouses.GetDefault();
            if (def != null) _warehouse.SelectedValue = def.Id;
        }
        Controls.Add(_warehouse);
        y += 35;

        var product = inv.Products.GetById(productId);
        _variant = new ComboBox { Location = new Point(140, y), Width = 260, DropDownStyle = ComboBoxStyle.DropDownList };
        var variants = new List<ProductVariant> { new() { Id = 0, Attributes = "(No variant)" } };
        variants.AddRange(inv.Products.GetVariants(productId).Where(v => v.IsActive));
        _variant.DataSource = variants;
        _variant.DisplayMember = nameof(ProductVariant.Attributes);
        _variant.ValueMember = nameof(ProductVariant.Id);
        _variant.SelectedValue = _b.VariantId ?? 0;
        Controls.Add(new Label { Text = "Variant:", Location = new Point(20, y + 3), AutoSize = true });
        Controls.Add(_variant);
        y += 35;

        Controls.Add(new Label { Text = "Manufacture:", Location = new Point(20, y + 3), AutoSize = true });
        _manufacture = new DateTimePicker { Location = new Point(140, y), Width = 260, Format = DateTimePickerFormat.Short,
            Value = _b.ManufactureDate ?? DateTime.Today };
        Controls.Add(_manufacture);
        y += 35;

        _hasExpiry = new CheckBox { Text = "Has expiry date", Location = new Point(140, y), Width = 260,
            Checked = _b.ExpiryDate.HasValue };
        Controls.Add(_hasExpiry);
        y += 30;

        Controls.Add(new Label { Text = "Expiry:", Location = new Point(20, y + 3), AutoSize = true });
        _expiry = new DateTimePicker { Location = new Point(140, y), Width = 260, Format = DateTimePickerFormat.Short,
            Value = _b.ExpiryDate ?? DateTime.Today.AddMonths(6), Enabled = _hasExpiry.Checked };
        _hasExpiry.CheckedChanged += (s, e) => _expiry.Enabled = _hasExpiry.Checked;
        Controls.Add(_expiry);
        y += 45;

        var btnSave = new Button { Text = "Save", Location = new Point(140, y), Width = 120, Height = 32,
            BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var btnCancel = new Button { Text = "Cancel", Location = new Point(270, y), Width = 120, Height = 32,
            DialogResult = DialogResult.Cancel };

        btnSave.Click += (s, e) =>
        {
            try
            {
                _b.BatchNumber = _batch.Text.Trim();
                _b.LotNumber = _lot.Text.Trim();
                _b.Quantity = int.Parse(_qty.Text);
                _b.CostPrice = decimal.Parse(_cost.Text);
                _b.Supplier = _supplier.Text.Trim();
                _b.WarehouseId = _warehouse.SelectedValue is int wid ? wid : 1;
                var selectedVariantId = _variant.SelectedValue is int vid && vid > 0 ? vid : (int?)null;
                if (product?.HasVariants == true && selectedVariantId == null)
                    throw new ArgumentException("Select a variant for this product.");
                _b.VariantId = selectedVariantId;
                _b.ManufactureDate = _manufacture.Value;
                _b.ExpiryDate = _hasExpiry.Checked ? _expiry.Value : null;

                if (_b.Id == 0)
                {
                    if (_b.Quantity <= 0) throw new ArgumentException("Batch quantity must be greater than zero.");
                    _inv.StockService.Move(_b.ProductId, _b.VariantId, _b.WarehouseId,
                        TransactionType.StockIn, _b.Quantity, _userId, batch: _b);
                }
                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        };

        Controls.AddRange(new Control[] { btnSave, btnCancel });
        AcceptButton = btnSave; CancelButton = btnCancel;
    }

    private TextBox AddField(string label, ref int y, string value)
    {
        Controls.Add(new Label { Text = label, Location = new Point(20, y + 3), AutoSize = true });
        var tb = new TextBox { Location = new Point(140, y), Width = 260, Text = value };
        Controls.Add(tb);
        y += 35;
        return tb;
    }
}