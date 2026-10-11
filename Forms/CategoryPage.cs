using InventoryManager.Models;
using InventoryManager.Services;

namespace InventoryManager.Forms;

public class CategoryPage : UserControl
{
    private readonly InventoryService _inventory;
    private readonly DataGridView _grid = new();

    public CategoryPage(InventoryService inventory)
    {
        _inventory = inventory;
        Dock = DockStyle.Fill;
        BackColor = Color.White;

        var buttons = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 44, Padding = new Padding(8) };
        var add = new Button { Text = "Add category", AutoSize = true };
        var edit = new Button { Text = "Edit", AutoSize = true };
        var delete = new Button { Text = "Delete", AutoSize = true };
        var refresh = new Button { Text = "Refresh", AutoSize = true };
        add.Click += (_, _) => EditCategory(null);
        edit.Click += (_, _) => { if (SelectedCategory() is { } category) EditCategory(category); };
        delete.Click += (_, _) => DeleteCategory();
        refresh.Click += (_, _) => LoadCategories();
        buttons.Controls.AddRange(new Control[] { add, edit, delete, refresh });

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.RowHeadersVisible = false;
        _grid.CellDoubleClick += (_, _) => { if (SelectedCategory() is { } category) EditCategory(category); };
        Controls.Add(_grid);
        Controls.Add(buttons);
        LoadCategories();
    }

    private Category? SelectedCategory() => _grid.CurrentRow?.DataBoundItem as Category;

    private void LoadCategories()
    {
        _grid.DataSource = _inventory.Categories.GetAll();
        foreach (var name in new[] { "Id", "ParentId", "SubCategories" })
            if (_grid.Columns.Contains(name)) _grid.Columns[name].Visible = false;
    }

    private void EditCategory(Category? existing)
    {
        var all = _inventory.Categories.GetAll();
        using var dialog = new Form
        {
            Text = existing == null ? "Add category" : "Edit category",
            Width = 390,
            Height = 245,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MinimizeBox = false,
            MaximizeBox = false
        };
        var name = new TextBox { Left = 130, Top = 20, Width = 210, Text = existing?.Name ?? "" };
        var description = new TextBox { Left = 130, Top = 55, Width = 210, Text = existing?.Description ?? "" };
        var parent = new ComboBox { Left = 130, Top = 90, Width = 210, DropDownStyle = ComboBoxStyle.DropDownList };
        var options = new List<Category> { new() { Id = 0, Name = "(Top-level)" } };
        options.AddRange(all.Where(c => c.Id != existing?.Id));
        parent.DataSource = options;
        parent.DisplayMember = nameof(Category.FullPath);
        parent.ValueMember = nameof(Category.Id);
        parent.SelectedValue = existing?.ParentId ?? 0;
        dialog.Controls.AddRange(new Control[]
        {
            new Label { Text = "Name:", Left = 20, Top = 23, AutoSize = true }, name,
            new Label { Text = "Description:", Left = 20, Top = 58, AutoSize = true }, description,
            new Label { Text = "Parent:", Left = 20, Top = 93, AutoSize = true }, parent
        });
        var save = new Button { Text = "Save", Left = 130, Top = 140, Width = 95, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", Left = 235, Top = 140, Width = 95, DialogResult = DialogResult.Cancel };
        dialog.Controls.AddRange(new Control[] { save, cancel });
        dialog.AcceptButton = save;
        dialog.CancelButton = cancel;

        if (dialog.ShowDialog(FindForm()) != DialogResult.OK) return;
        if (string.IsNullOrWhiteSpace(name.Text))
        {
            MessageBox.Show(FindForm(), "Category name is required.", "Invalid category", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        var category = existing ?? new Category();
        category.Name = name.Text.Trim();
        category.Description = description.Text.Trim();
        category.ParentId = parent.SelectedValue is int id && id != 0 ? id : null;
        try
        {
            if (existing == null) _inventory.Categories.Add(category);
            else _inventory.Categories.Update(category);
            LoadCategories();
        }
        catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Category error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }

    private void DeleteCategory()
    {
        if (SelectedCategory() is not { } category) return;
        if (MessageBox.Show(FindForm(), $"Delete '{category.Name}'? Its subcategories will become top-level.", "Confirm",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
        try
        {
            _inventory.Categories.Delete(category.Id);
            LoadCategories();
        }
        catch (Exception ex) { MessageBox.Show(FindForm(), ex.Message, "Category error", MessageBoxButtons.OK, MessageBoxIcon.Error); }
    }
}
