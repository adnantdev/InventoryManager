using System.Drawing.Drawing2D;

namespace InventoryManager.Forms;

internal static class CommerceVisuals
{
    public static readonly Color Ink = Color.FromArgb(35, 48, 65);
    public static readonly Color Accent = Color.FromArgb(0, 120, 215);
    public static readonly Color Canvas = Color.FromArgb(245, 247, 250);

    public static Label Heading(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        Height = 48,
        Padding = new Padding(18, 8, 0, 0),
        Font = new Font("Segoe UI", 18, FontStyle.Bold),
        ForeColor = Ink
    };

    public static DataGridView Grid()
    {
        var grid = new DataGridView
        {
            Dock = DockStyle.Fill,
            ReadOnly = true,
            AutoGenerateColumns = true,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            MultiSelect = false,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
            BackgroundColor = Color.White,
            BorderStyle = BorderStyle.None,
            RowHeadersVisible = false,
            EnableHeadersVisualStyles = false,
            ColumnHeadersHeight = 36
        };
        grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Ink,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        grid.DefaultCellStyle = new DataGridViewCellStyle
        {
            SelectionBackColor = Color.FromArgb(218, 237, 252),
            SelectionForeColor = Ink,
            Padding = new Padding(4)
        };
        grid.AlternatingRowsDefaultCellStyle.BackColor = Color.FromArgb(248, 250, 252);
        return grid;
    }

    public static Button Button(string text, EventHandler onClick)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            Height = 34,
            Padding = new Padding(10, 3, 10, 3),
            FlatStyle = FlatStyle.Flat,
            BackColor = Accent,
            ForeColor = Color.White,
            Font = new Font("Segoe UI", 9, FontStyle.Bold),
            Margin = new Padding(4)
        };
        button.FlatAppearance.BorderSize = 0;
        button.Click += onClick;
        return button;
    }

    public static Panel Card(string title, string value, Color accent)
    {
        var card = new Panel
        {
            BackColor = Color.White,
            Margin = new Padding(8),
            Padding = new Padding(16),
            Height = 112,
            MinimumSize = new Size(150, 105)
        };
        card.Paint += (_, e) =>
        {
            using var brush = new SolidBrush(accent);
            e.Graphics.FillRectangle(brush, 0, 0, 5, card.Height);
        };
        var titleLabel = new Label
        {
            Text = title.ToUpperInvariant(),
            Dock = DockStyle.Top,
            Height = 27,
            ForeColor = Color.FromArgb(105, 116, 130),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        };
        var valueLabel = new Label
        {
            Text = value,
            Dock = DockStyle.Fill,
            ForeColor = Ink,
            Font = new Font("Segoe UI", 21, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true
        };
        card.Controls.Add(valueLabel);
        card.Controls.Add(titleLabel);
        return card;
    }
}

internal sealed class BarChartControl : Control
{
    private IReadOnlyList<(string Label, decimal Value)> _items = Array.Empty<(string, decimal)>();

    public BarChartControl()
    {
        DoubleBuffered = true;
        BackColor = Color.White;
        MinimumSize = new Size(240, 180);
    }

    public void SetData(IEnumerable<(string Label, decimal Value)> items)
    {
        _items = items.ToList();
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0 || e.ClipRectangle.IsEmpty) return;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);
        if (_items.Count == 0)
        {
            TextRenderer.DrawText(e.Graphics, "No report data for this period.", Font, ClientRectangle,
                Color.Gray, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        var visible = _items.Take(10).ToList();
        var labelWidth = Math.Min(135, Math.Max(72, Width / 3));
        var valueWidth = 78;
        var left = labelWidth + 8;
        var available = Math.Max(0, Width - left - valueWidth - 12);
        if (available == 0) return;
        var rowHeight = Math.Max(1, (Height - 14) / visible.Count);
        var max = Math.Max(1d, visible.Max(item => Math.Abs((double)item.Value)));
        using var barBrush = new SolidBrush(CommerceVisuals.Accent);
        using var negativeBrush = new SolidBrush(Color.FromArgb(220, 90, 80));
        using var textBrush = new SolidBrush(CommerceVisuals.Ink);

        for (var i = 0; i < visible.Count; i++)
        {
            var item = visible[i];
            var y = 8 + i * rowHeight;
            var label = item.Label.Length > 18 ? item.Label[..17] + "…" : item.Label;
            TextRenderer.DrawText(e.Graphics, label, Font, new Rectangle(4, y, labelWidth, rowHeight),
                CommerceVisuals.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            var scaledWidth = available * Math.Abs((double)item.Value) / max;
            var width = item.Value == 0 ? 0 : Math.Clamp((int)Math.Round(scaledWidth), 1, available);
            using var brush = item.Value < 0 ? negativeBrush : barBrush;
            var barHeight = Math.Min(Math.Max(1, rowHeight - 10), Math.Max(1, Height - y - 5));
            if (width > 0 && barHeight > 0)
                e.Graphics.FillRectangle(brush, left, y + 5, width, barHeight);
            var valueText = item.Value.ToString("N0");
            TextRenderer.DrawText(e.Graphics, valueText, Font,
                new Rectangle(left + available + 6, y, valueWidth, rowHeight), textBrush.Color,
                TextFormatFlags.VerticalCenter | TextFormatFlags.Right);
        }
    }
}
