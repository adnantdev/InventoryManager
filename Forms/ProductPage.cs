using InventoryManager.Models;
using InventoryManager.Services;

namespace InventoryManager.Forms;

public class ProductPage : UserControl
{
    private readonly InventoryService _inv;
    private readonly AuthService _auth;
    private readonly ProductCsvService _productCsv;
    private DataGridView _grid = null!;
    private TextBox _txtSearch = null!;
    private ComboBox _cmbCategory = null!;

    public ProductPage(InventoryService inv, AuthService auth, ProductCsvService productCsv)
    {
        _inv = inv; _auth = auth; _productCsv = productCsv;
        Dock = DockStyle.Fill;
        BackColor = Color.White;
        Font = new Font("Segoe UI", 9);

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 82, Padding = new Padding(10), WrapContents = true, AutoScroll = true };

        _txtSearch = new TextBox
        {
            Width = 220, Location = new Point(10, 12),
            PlaceholderText = "Search name / SKU / barcode..."
        };
        _cmbCategory = new ComboBox
        {
            Width = 180, Location = new Point(240, 12),
            DropDownStyle = ComboBoxStyle.DropDownList
        };

        var btnSearch = new Button { Text = "Search", Location = new Point(430, 11), Width = 80, Height = 26 };
        var btnAdd    = new Button { Text = "+ Add Product", Location = new Point(520, 11), Width = 120, Height = 26,
            BackColor = Color.FromArgb(0, 120, 215), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
        var btnEdit   = new Button { Text = "Edit", Location = new Point(650, 11), Width = 70, Height = 26 };
        var btnDel    = new Button { Text = "Delete", Location = new Point(725, 11), Width = 70, Height = 26 };
        var btnRefresh= new Button { Text = "⟳", Location = new Point(800, 11), Width = 40, Height = 26 };
        var btnImport = new Button { Text = "Import CSV", AutoSize = true, Height = 26 };
        var btnExport = new Button { Text = "Export CSV", AutoSize = true, Height = 26 };

        btnSearch.Click  += (s, e) => LoadData();
        btnRefresh.Click += (s, e) => LoadData();
        _txtSearch.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) LoadData(); };
        _cmbCategory.SelectedIndexChanged += (s, e) => LoadData();
        btnAdd.Click  += (s, e) => Edit(null);
        btnEdit.Click += (s, e) => { if (_grid.CurrentRow?.DataBoundItem is Product p) Edit(p); };
        btnDel.Click  += (s, e) => DeleteSelected();
        btnImport.Click += (_, _) => ImportCsv();
        btnExport.Click += (_, _) => ExportCsv();

        top.Controls.AddRange(new Control[] { _txtSearch, _cmbCategory, btnSearch, btnAdd, btnEdit, btnDel, btnRefresh, btnImport, btnExport });

        _grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AllowUserToAddRows = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = false,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            RowHeadersVisible = false,
            BackgroundColor = Color.White,
            AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle { BackColor = Color.FromArgb(245, 248, 252) },
            AllowUserToResizeRows = false
        };
        _grid.CellDoubleClick += (s, e) => { if (_grid.CurrentRow?.DataBoundItem is Product p) Edit(p); };

        Controls.Add(_grid);
        Controls.Add(top);

        LoadCategories();
        LoadData();
    }

    private void LoadCategories()
    {
        var cats = _inv.Categories.GetAll();
        var all = new List<Category> { new() { Id = 0, Name = "(All Categories)" } };
        all.AddRange(cats);
        _cmbCategory.DataSource = all;
        _cmbCategory.DisplayMember = "Name";
        _cmbCategory.ValueMember = "Id";
    }

    private void LoadData()
    {
        int cid = _cmbCategory.SelectedValue is int id ? id : 0;
        _grid.DataSource = _inv.Products.GetAll(_txtSearch.Text, cid);

        if (_grid.Columns.Contains("Id")) _grid.Columns["Id"].Visible = false;
        if (_grid.Columns.Contains("CategoryId")) _grid.Columns["CategoryId"].Visible = false;
        if (_grid.Columns.Contains("UnitId")) _grid.Columns["UnitId"].Visible = false;
        if (_grid.Columns.Contains("Description")) _grid.Columns["Description"].Visible = false;
        if (_grid.Columns.Contains("Variants")) _grid.Columns["Variants"].Visible = false;
        if (_grid.Columns.Contains("StockValue")) _grid.Columns["StockValue"].Visible = false;
        if (_grid.Columns.Contains("IsLowStock")) _grid.Columns["IsLowStock"].Visible = false;
        if (_grid.Columns.Contains("IsOutOfStock")) _grid.Columns["IsOutOfStock"].Visible = false;

        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is Product p)
            {
                if (p.IsOutOfStock)
                    row.DefaultCellStyle.BackColor = Color.FromArgb(255, 220, 220);
                else if (p.IsLowStock)
                    row.DefaultCellStyle.BackColor = Color.FromArgb(255, 245, 210);
            }
        }
    }

    private void Edit(Product? p)
    {
        using var dlg = new ProductEditDialog(_inv, _auth, p);
        if (dlg.ShowDialog(FindForm()) == DialogResult.OK) LoadData();
    }

    private void DeleteSelected()
    {
        if (_grid.CurrentRow?.DataBoundItem is not Product p) return;
        if (MessageBox.Show(FindForm(), $"Delete '{p.Name}'?", "Confirm",
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            _inv.Products.Delete(p.Id);
            LoadData();
        }
        catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message); }
    }

    private void ImportCsv()
    {
        using var dialog = new OpenFileDialog { Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*" };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            var result = _productCsv.Import(dialog.FileName);
            LoadData();
            MessageBox.Show(FindForm(), $"Processed {result.Rows} rows: {result.Imported} imported, {result.Updated} updated.",
                "Product import", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(FindForm(), exception.Message, "Product import failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportCsv()
    {
        using var dialog = new SaveFileDialog
        {
            Filter = "CSV files (*.csv)|*.csv",
            DefaultExt = "csv",
            AddExtension = true,
            FileName = $"products-{DateTime.Today:yyyyMMdd}.csv"
        };
        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            _productCsv.Export(dialog.FileName);
            MessageBox.Show(FindForm(), "Product data exported.", "Product export", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception exception)
        {
            MessageBox.Show(FindForm(), exception.Message, "Product export failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}