using InventoryManager.Models;
using InventoryManager.Services;

namespace InventoryManager.Forms;

public class VariantEditDialog : Form
{
    private readonly InventoryService _inv;
    private readonly ProductVariant _v;

    private TextBox _sku = null!, _barcode = null!, _attrs = null!, _delta = null!;
    private CheckBox _active = null!;

    public VariantEditDialog(InventoryService inv, int productId, ProductVariant? existing)
    {
        _inv = inv;
        _v = existing ?? new ProductVariant { ProductId = productId };
        Text = existing == null ? "Add Variant" : "Edit Variant";
        Size = new Size(420, 300);
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false; MinimizeBox = false;

        int y = 20;
        _sku     = AddField("SKU:", ref y, _v.SKU);
        _barcode = AddField("Barcode:", ref y, _v.Barcode);
        _attrs   = AddField("Attributes:", ref y, _v.Attributes);
        _delta   = AddField("Price delta:", ref y, _v.PriceDelta.ToString("0.##"));

        _active = new CheckBox { Text = "Active", Location = new Point(120, y), Width = 200, Checked = _v.IsActive };
        Controls.Add(_active);

        var btnSave = new Button { Text = "Save", Location = new Point(120, y + 40), Width = 100, Height = 30,
            BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var btnCancel = new Button { Text = "Cancel", Location = new Point(230, y + 40), Width = 100, Height = 30,
            DialogResult = DialogResult.Cancel };

        btnSave.Click += (s, e) =>
        {
            try
            {
                _v.SKU = _sku.Text.Trim();
                _v.Barcode = _barcode.Text.Trim();
                _v.Attributes = _attrs.Text.Trim();
                _v.PriceDelta = decimal.Parse(_delta.Text);
                _v.IsActive = _active.Checked;

                if (string.IsNullOrWhiteSpace(_v.SKU)) throw new Exception("SKU is required.");

                if (_v.Id == 0) _inv.Products.AddVariant(_v);
                else _inv.Products.UpdateVariant(_v);

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
        var tb = new TextBox { Location = new Point(120, y), Width = 260, Text = value };
        Controls.Add(tb);
        y += 35;
        return tb;
    }
}