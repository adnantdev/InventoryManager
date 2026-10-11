using InventoryManager.Models;
using InventoryManager.Services;

namespace InventoryManager.Forms;

public class BatchForm : Form
{
    private readonly InventoryService _inventory;
    private readonly AuthService _auth;
    private readonly ComboBox _product = new();
    private readonly ComboBox _warehouse = new();
    private readonly DataGridView _grid = new();

    public BatchForm(InventoryService inventory, AuthService auth)
    {
        _inventory = inventory;
        _auth = auth;
        Text = "Batch and lot tracking";
        Width = 1100;
        Height = 620;
        StartPosition = FormStartPosition.CenterParent;
        var top = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 50, Padding = new Padding(8), WrapContents = false };
        _product.Width = 250;
        _product.DropDownStyle = ComboBoxStyle.DropDownList;
        var products = new List<Product> { new() { Id = 0, Name = "(All products)" } };
        products.AddRange(inventory.Products.GetAll().Where(p => p.IsActive));
        _product.DataSource = products;
        _product.DisplayMember = nameof(Product.Name);
        _product.SelectedIndexChanged += (_, _) => LoadBatches();
        _warehouse.Width = 200;
        _warehouse.DropDownStyle = ComboBoxStyle.DropDownList;
        var warehouses = new List<Warehouse> { new() { Id = 0, Name = "(All locations)" } };
        warehouses.AddRange(inventory.Warehouses.GetAll());
        _warehouse.DataSource = warehouses;
        _warehouse.DisplayMember = nameof(Warehouse.Name);
        _warehouse.ValueMember = nameof(Warehouse.Id);
        _warehouse.SelectedIndexChanged += (_, _) => LoadBatches();
        var add = new Button { Text = "Receive batch", AutoSize = true };
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        add.Click += (_, _) => AddBatch();
        refresh.Click += (_, _) => LoadBatches();
        top.Controls.AddRange(new Control[]
        {
            new Label { Text = "Product:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) }, _product,
            new Label { Text = "Location:", AutoSize = true, Padding = new Padding(8, 6, 0, 0) }, _warehouse,
            add, refresh
        });
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowHeadersVisible = false;
        Controls.Add(_grid);
        Controls.Add(top);
        LoadBatches();
    }

    private void LoadBatches()
    {
        var product = _product.SelectedItem as Product;
        var warehouseId = _warehouse.SelectedValue is int id && id > 0 ? id : (int?)null;
        var batches = product is { Id: > 0 }
            ? _inventory.Batches.GetForProduct(product.Id, warehouseId)
            : _inventory.Products.GetAll().SelectMany(p => _inventory.Batches.GetForProduct(p.Id, warehouseId)).ToList();
        _grid.DataSource = batches;
        foreach (var name in new[] { "Id", "ProductId", "VariantId", "WarehouseId" })
            if (_grid.Columns.Contains(name)) _grid.Columns[name].Visible = false;
        foreach (DataGridViewRow row in _grid.Rows)
        {
            if (row.DataBoundItem is StockBatch batch && batch.IsExpired)
                row.DefaultCellStyle.BackColor = Color.FromArgb(255, 220, 220);
        }
    }

    private void AddBatch()
    {
        if (_product.SelectedItem is not Product { Id: > 0 } product)
        {
            MessageBox.Show(this, "Select a product before receiving a batch.", "Select product",
                MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dialog = new BatchEditDialog(_inventory, product.Id, null, _auth.CurrentUser?.Id ?? 0);
        if (dialog.ShowDialog(this) == DialogResult.OK) LoadBatches();
    }
}
