using InventoryManager.Models;
using InventoryManager.Services;

namespace InventoryManager.Forms;

public class ProductEditDialog : Form
{
    private readonly InventoryService _inv;
    private readonly AuthService _auth;
    private readonly Product _product;

    private TextBox _sku = null!, _name = null!, _barcode = null!, _desc = null!;
    private TextBox _cost = null!, _sell = null!, _reorder = null!;
    private ComboBox _cmbCat = null!, _cmbUnit = null!;
    private CheckBox _chkBatches = null!, _chkExpiry = null!, _chkVariants = null!;

    private DataGridView _gridVariants = null!;
    private DataGridView _gridBatches = null!;

    public ProductEditDialog(InventoryService inv, AuthService auth, Product? existing)
    {
        _inv = inv; _auth = auth;
        _product = existing ?? new Product();

        Text = existing == null ? "Add Product" : $"Edit Product — {_product.Name}";
        Size = new Size(700, 640);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(600, 500);

        var tabs = new TabControl { Dock = DockStyle.Fill };
        var tabGeneral = new TabPage("General");
        var tabVariants = new TabPage("Variants");
        var tabBatches = new TabPage("Batches / Lots");
        tabs.TabPages.AddRange(new[] { tabGeneral, tabVariants, tabBatches });

        // ---------- General tab ----------
        int y = 20;
        _sku     = AddField(tabGeneral, "SKU:",     ref y, _product.SKU);
        _barcode = AddField(tabGeneral, "Barcode:", ref y, _product.Barcode);
        _name    = AddField(tabGeneral, "Name:",    ref y, _product.Name);

        tabGeneral.Controls.Add(new Label { Text = "Description:", Location = new Point(20, y + 3), AutoSize = true });
        _desc = new TextBox { Location = new Point(140, y), Width = 500, Height = 60, Multiline = true };
        _desc.Text = _product.Description;
        tabGeneral.Controls.Add(_desc);
        y += 70;

        tabGeneral.Controls.Add(new Label { Text = "Category:", Location = new Point(20, y + 3), AutoSize = true });
        _cmbCat = new ComboBox { Location = new Point(140, y), Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
        tabGeneral.Controls.Add(_cmbCat);
        y += 35;

        tabGeneral.Controls.Add(new Label { Text = "Unit:", Location = new Point(20, y + 3), AutoSize = true });
        _cmbUnit = new ComboBox { Location = new Point(140, y), Width = 240, DropDownStyle = ComboBoxStyle.DropDownList };
        tabGeneral.Controls.Add(_cmbUnit);
        y += 35;

        _cost    = AddField(tabGeneral, "Cost Price:",    ref y, _product.CostPrice.ToString("0.##"));
        _sell    = AddField(tabGeneral, "Sell Price:",    ref y, _product.SellPrice.ToString("0.##"));
        _reorder = AddField(tabGeneral, "Reorder Level:", ref y, _product.ReorderLevel.ToString());

        _chkBatches  = new CheckBox { Text = "Track batches / lots", Location = new Point(20, y), Width = 250, Checked = _product.TrackBatches };
        y += 30;
        _chkExpiry   = new CheckBox { Text = "Track expiry dates",   Location = new Point(20, y), Width = 250, Checked = _product.TrackExpiry };
        y += 30;
        _chkVariants = new CheckBox { Text = "Has variants",          Location = new Point(20, y), Width = 250, Checked = _product.HasVariants };

        tabGeneral.Controls.AddRange(new Control[] { _chkBatches, _chkExpiry, _chkVariants });

        // Load categories and units
        var cats = new List<Category> { new() { Id = 0, Name = "(None)" } };
        cats.AddRange(_inv.Categories.GetAll());
        _cmbCat.DataSource = cats;
        _cmbCat.DisplayMember = "Name";
        _cmbCat.ValueMember = "Id";
        if (_product.CategoryId is int cid && cid > 0) _cmbCat.SelectedValue = cid;

        var units = _inv.Units.GetAll();
        _cmbUnit.DataSource = units;
        _cmbUnit.DisplayMember = "Code";
        _cmbUnit.ValueMember = "Id";
        if (_product.UnitId > 0) _cmbUnit.SelectedValue = _product.UnitId;

        // ---------- Variants tab ----------
        var varPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        var varTop = new Panel { Dock = DockStyle.Top, Height = 40 };
        var btnAddVar = new Button { Text = "+ Add Variant", Location = new Point(0, 5), Width = 130, Height = 28 };
        var btnEditVar = new Button { Text = "Edit", Location = new Point(140, 5), Width = 80, Height = 28 };
        var btnDelVar = new Button { Text = "Delete", Location = new Point(230, 5), Width = 80, Height = 28 };
        varTop.Controls.AddRange(new Control[] { btnAddVar, btnEditVar, btnDelVar });

        _gridVariants = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false, BackgroundColor = Color.White
        };
        varPanel.Controls.Add(_gridVariants);
        varPanel.Controls.Add(varTop);
        tabVariants.Controls.Add(varPanel);

        btnAddVar.Click += (s, e) =>
        {
            if (_product.Id == 0) { MessageBox.Show("Save the product first."); return; }
            using var d = new VariantEditDialog(_inv, _product.Id, null);
            if (d.ShowDialog(this) == DialogResult.OK) LoadVariants();
        };
        btnEditVar.Click += (s, e) =>
        {
            if (_gridVariants.CurrentRow?.DataBoundItem is ProductVariant v)
            {
                using var d = new VariantEditDialog(_inv, _product.Id, v);
                if (d.ShowDialog(this) == DialogResult.OK) LoadVariants();
            }
        };
        btnDelVar.Click += (s, e) =>
        {
            if (_gridVariants.CurrentRow?.DataBoundItem is ProductVariant v)
            {
                if (MessageBox.Show($"Delete variant {v.Attributes}?", "Confirm",
                    MessageBoxButtons.YesNo) == DialogResult.Yes)
                { _inv.Products.DeleteVariant(v.Id); LoadVariants(); }
            }
        };

        // ---------- Batches tab ----------
        var batchPanel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(10) };
        var batchTop = new Panel { Dock = DockStyle.Top, Height = 40 };
        var btnAddBatch = new Button { Text = "+ Add Batch", Location = new Point(0, 5), Width = 120, Height = 28 };
        batchTop.Controls.Add(btnAddBatch);

        _gridBatches = new DataGridView
        {
            Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false, BackgroundColor = Color.White
        };
        batchPanel.Controls.Add(_gridBatches);
        batchPanel.Controls.Add(batchTop);
        tabBatches.Controls.Add(batchPanel);

        btnAddBatch.Click += (s, e) =>
        {
            if (_product.Id == 0) { MessageBox.Show("Save the product first."); return; }
            using var d = new BatchEditDialog(_inv, _product.Id, null, _auth.CurrentUser?.Id ?? 0);
            if (d.ShowDialog(this) == DialogResult.OK) LoadBatches();
        };

        // ---------- Save / Cancel ----------
        var btnSave = new Button
        {
            Text = "Save", Width = 100, Height = 32,
            BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White,
            FlatStyle = FlatStyle.Flat
        };
        var btnCancel = new Button { Text = "Cancel", Width = 100, Height = 32, DialogResult = DialogResult.Cancel };

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Bottom, Height = 55, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(10)
        };
        bottom.Controls.Add(btnCancel);
        bottom.Controls.Add(btnSave);

        btnSave.Click += (s, e) => Save();

        Controls.Add(tabs);
        Controls.Add(bottom);
        CancelButton = btnCancel;

        if (existing != null)
        {
            LoadVariants();
            LoadBatches();
        }
        else
        {
            tabVariants.Enabled = false;
            tabBatches.Enabled = false;
        }
    }

    private void Save()
    {
        try
        {
            _product.SKU = _sku.Text.Trim();
            _product.Barcode = _barcode.Text.Trim();
            _product.Name = _name.Text.Trim();
            _product.Description = _desc.Text.Trim();
            _product.CategoryId = _cmbCat.SelectedValue is int cid && cid > 0 ? cid : null;
            _product.UnitId = _cmbUnit.SelectedValue is int uid ? uid : 1;
            _product.CostPrice = decimal.Parse(_cost.Text);
            _product.SellPrice = decimal.Parse(_sell.Text);
            _product.ReorderLevel = int.Parse(_reorder.Text);
            _product.TrackBatches = _chkBatches.Checked;
            _product.TrackExpiry = _chkExpiry.Checked;
            _product.HasVariants = _chkVariants.Checked;

            if (string.IsNullOrWhiteSpace(_product.Name) || string.IsNullOrWhiteSpace(_product.SKU))
                throw new Exception("SKU and Name are required.");

            if (_product.Id == 0)
            {
                _product.Id = _inv.Products.Add(_product);
                // Enable variant/batch tabs
                ((TabControl)Controls.OfType<TabControl>().First()).TabPages[1].Enabled = true;
                ((TabControl)Controls.OfType<TabControl>().First()).TabPages[2].Enabled = true;
                DialogResult = DialogResult.OK;   // still close? Let's keep open for variants
                // Uncomment to allow continuing to add variants:
                // LoadVariants(); LoadBatches(); return;
            }
            else
            {
                _inv.Products.Update(_product);
            }

            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void LoadVariants()
    {
        _gridVariants.DataSource = _inv.Products.GetVariants(_product.Id);
        if (_gridVariants.Columns.Contains("Id")) _gridVariants.Columns["Id"].Visible = false;
        if (_gridVariants.Columns.Contains("ProductId")) _gridVariants.Columns["ProductId"].Visible = false;
    }

    private void LoadBatches()
    {
        _gridBatches.DataSource = _inv.Batches.GetForProduct(_product.Id);
        if (_gridBatches.Columns.Contains("Id")) _gridBatches.Columns["Id"].Visible = false;
        if (_gridBatches.Columns.Contains("ProductId")) _gridBatches.Columns["ProductId"].Visible = false;
        if (_gridBatches.Columns.Contains("VariantId")) _gridBatches.Columns["VariantId"].Visible = false;
        if (_gridBatches.Columns.Contains("WarehouseId")) _gridBatches.Columns["WarehouseId"].Visible = false;

        foreach (DataGridViewRow row in _gridBatches.Rows)
        {
            if (row.DataBoundItem is StockBatch b)
            {
                if (b.IsExpired) row.DefaultCellStyle.BackColor = Color.FromArgb(255, 200, 200);
                else if (b.IsExpiringSoon()) row.DefaultCellStyle.BackColor = Color.FromArgb(255, 245, 210);
            }
        }
    }

    private TextBox AddField(Control parent, string label, ref int y, string value)
    {
        parent.Controls.Add(new Label { Text = label, Location = new Point(20, y + 3), AutoSize = true });
        var tb = new TextBox { Location = new Point(140, y), Width = 500, Text = value };
        parent.Controls.Add(tb);
        y += 35;
        return tb;
    }
}