using InventoryManager.Models;
using InventoryManager.Services;

namespace InventoryManager.Forms;

public class BarcodePage : UserControl
{
    private readonly InventoryService _inventory;
    private readonly ComboBox _product = new();
    private readonly ComboBox _variant = new();
    private readonly ComboBox _format = new();
    private readonly TextBox _content = new();
    private readonly TextBox _scannerInput = new() { Width = 180, PlaceholderText = "Scan barcode and press Enter" };
    private readonly Label _scanStatus = new() { AutoSize = true, Padding = new Padding(0, 7, 0, 0) };
    private readonly PictureBox _preview = new();
    private Bitmap? _generated;

    public BarcodePage(InventoryService inventory)
    {
        _inventory = inventory;
        Dock = DockStyle.Fill;
        BackColor = Color.White;

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 92, Padding = new Padding(10), WrapContents = true };
        _product.Width = 200;
        _product.DropDownStyle = ComboBoxStyle.DropDownList;
        _product.DataSource = inventory.Products.GetAll().Where(p => p.IsActive).ToList();
        _product.DisplayMember = nameof(Product.Name);
        _product.SelectedIndexChanged += (_, _) => LoadVariants();
        _variant.Width = 200;
        _variant.DropDownStyle = ComboBoxStyle.DropDownList;
        _variant.SelectedIndexChanged += (_, _) => UpdateContent();
        _format.Width = 120;
        _format.DropDownStyle = ComboBoxStyle.DropDownList;
        _format.Items.AddRange(new object[] { "Barcode", "QR code" });
        _format.SelectedIndex = 0;
        var generate = new Button { Text = "Generate", AutoSize = true };
        var save = new Button { Text = "Save image", AutoSize = true };
        var scannerLabel = new Label { Text = "Scanner:", AutoSize = true, Padding = new Padding(8, 7, 0, 0) };
        generate.Click += (_, _) => Generate();
        save.Click += (_, _) => SaveImage();
        _scannerInput.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            e.SuppressKeyPress = true;
            FindScannedProduct();
        };
        top.Controls.AddRange(new Control[]
        {
            new Label { Text = "Product:", AutoSize = true, Padding = new Padding(0, 7, 0, 0) }, _product,
            _variant, _format, generate, save, scannerLabel, _scannerInput, _scanStatus
        });
        _content.Dock = DockStyle.Top;
        _content.ReadOnly = true;
        _content.Height = 32;
        _content.Margin = new Padding(10);
        _preview.Dock = DockStyle.Fill;
        _preview.SizeMode = PictureBoxSizeMode.CenterImage;
        _preview.BackColor = Color.White;
        _preview.BorderStyle = BorderStyle.FixedSingle;
        Controls.Add(_preview);
        Controls.Add(_content);
        Controls.Add(top);
        LoadVariants();
        _scannerInput.Focus();
        Disposed += (_, _) => _generated?.Dispose();
    }

    private void FindScannedProduct()
    {
        var code = _scannerInput.Text.Trim();
        _scannerInput.Clear();
        if (code.Length == 0) return;
        var product = _inventory.Products.GetByBarcode(code);
        if (product == null)
        {
            foreach (var candidate in _inventory.Products.GetAll())
            {
                var variant = _inventory.Products.GetVariants(candidate.Id)
                    .FirstOrDefault(item => item.IsActive && string.Equals(item.Barcode, code, StringComparison.OrdinalIgnoreCase));
                if (variant == null) continue;
                product = candidate;
                _product.SelectedItem = candidate;
                _variant.SelectedItem = _variant.Items.Cast<ProductVariant>()
                    .FirstOrDefault(item => item.Id == variant.Id);
                break;
            }
        }
        if (product == null)
        {
            _scanStatus.ForeColor = Color.Firebrick;
            _scanStatus.Text = "Barcode not found";
            return;
        }
        _product.SelectedItem = product;
        _scanStatus.ForeColor = Color.DarkGreen;
        _scanStatus.Text = $"Found {product.Name}";
        _scannerInput.Focus();
    }

    private void LoadVariants()
    {
        var variants = new List<ProductVariant> { new() { Id = 0, Attributes = "(Product barcode)" } };
        if (_product.SelectedItem is Product product)
            variants.AddRange(_inventory.Products.GetVariants(product.Id).Where(v => v.IsActive));
        _variant.DataSource = variants;
        UpdateContent();
    }

    private void UpdateContent()
    {
        if (_product.SelectedItem is not Product product) { _content.Text = ""; return; }
        var variant = _variant.SelectedItem as ProductVariant;
        _content.Text = variant is { Id: > 0 } ? variant.Barcode : product.Barcode;
    }

    private void Generate()
    {
        if (string.IsNullOrWhiteSpace(_content.Text))
        {
            MessageBox.Show(FindForm(), "The selected product or variant has no barcode value.", "Missing barcode",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        try
        {
            var next = _format.SelectedIndex == 1
                ? BarcodeService.GenerateQr(_content.Text)
                : BarcodeService.GenerateBarcode(_content.Text);
            var previous = _generated;
            _generated = next;
            _preview.Image = next;
            previous?.Dispose();
        }
        catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Code generation failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void SaveImage()
    {
        if (_generated == null)
        {
            MessageBox.Show(FindForm(), "Generate a code before saving.", "No image", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new SaveFileDialog
        {
            Filter = "PNG image (*.png)|*.png",
            FileName = $"{(_product.SelectedItem as Product)?.SKU ?? "code"}.png"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try { _generated.Save(dialog.FileName, System.Drawing.Imaging.ImageFormat.Png); }
        catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Save failed", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
