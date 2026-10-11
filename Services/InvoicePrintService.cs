using System.Drawing.Printing;
using InventoryManager.Models;

namespace InventoryManager.Services;

public static class InvoicePrintService
{
    public static void PreviewSalesInvoice(IWin32Window? owner, SalesInvoiceSummary invoice, IReadOnlyList<CommerceLine> lines)
    {
        var lineIndex = 0;
        using var document = new PrintDocument();
        document.DocumentName = $"Invoice {invoice.Number}";
        document.DefaultPageSettings.Margins = new Margins(48, 48, 48, 48);
        document.PrintPage += (_, e) =>
        {
            var graphics = e.Graphics ?? throw new InvalidOperationException("Printer graphics surface is unavailable.");
            using var titleFont = new Font("Segoe UI", 20, FontStyle.Bold);
            using var headerFont = new Font("Segoe UI", 10, FontStyle.Bold);
            using var bodyFont = new Font("Segoe UI", 9);
            using var brush = new SolidBrush(Color.Black);
            var left = e.MarginBounds.Left;
            var right = e.MarginBounds.Right;
            var y = e.MarginBounds.Top;
            graphics.DrawString("SALES INVOICE", titleFont, brush, left, y);
            y += 38;
            graphics.DrawString($"Invoice: {invoice.Number}", bodyFont, brush, left, y);
            graphics.DrawString($"Date: {invoice.InvoiceDate:g}", bodyFont, brush, right - 220, y);
            y += 22;
            graphics.DrawString($"Customer: {invoice.CustomerName}", bodyFont, brush, left, y);
            y += 34;
            graphics.DrawString("Product", headerFont, brush, left, y);
            graphics.DrawString("Qty", headerFont, brush, right - 225, y);
            graphics.DrawString("Unit price", headerFont, brush, right - 165, y);
            graphics.DrawString("Amount", headerFont, brush, right - 82, y);
            y += 22;
            graphics.DrawLine(Pens.Gray, left, y, right, y);
            y += 8;

            while (lineIndex < lines.Count && y + 38 < e.MarginBounds.Bottom - 100)
            {
                var line = lines[lineIndex++];
                var description = string.IsNullOrWhiteSpace(line.VariantName)
                    ? line.ProductName
                    : $"{line.ProductName} ({line.VariantName})";
                graphics.DrawString(description, bodyFont, brush, new RectangleF(left, y, right - left - 240, 32));
                graphics.DrawString(line.Quantity.ToString(), bodyFont, brush, right - 225, y);
                graphics.DrawString(line.UnitPrice.ToString("C"), bodyFont, brush, right - 165, y);
                graphics.DrawString(line.LineTotal.ToString("C"), bodyFont, brush, right - 82, y);
                y += 36;
            }
            if (lineIndex < lines.Count)
            {
                e.HasMorePages = true;
                return;
            }

            y += 8;
            graphics.DrawLine(Pens.Gray, left, y, right, y);
            y += 15;
            DrawTotal(graphics, "Subtotal", invoice.Subtotal, right - 220, y, bodyFont, brush);
            y += 20;
            DrawTotal(graphics, $"Discount ({invoice.DiscountPercent:0.##}%)",
                invoice.Subtotal * invoice.DiscountPercent / 100, right - 220, y, bodyFont, brush);
            y += 20;
            DrawTotal(graphics, $"Tax ({invoice.TaxPercent:0.##}%)", invoice.TaxAmount, right - 220, y, bodyFont, brush);
            y += 23;
            DrawTotal(graphics, "Total", invoice.Total, right - 220, y, headerFont, brush);
            y += 23;
            DrawTotal(graphics, "Paid", invoice.PaidAmount, right - 220, y, bodyFont, brush);
            y += 20;
            DrawTotal(graphics, "Balance due", invoice.BalanceDue, right - 220, y, headerFont, brush);
            e.HasMorePages = false;
        };

        using var preview = new PrintPreviewDialog
        {
            Document = document,
            Width = 1050,
            Height = 760,
            StartPosition = FormStartPosition.CenterParent
        };
        preview.ShowDialog(owner);
    }

    private static void DrawTotal(Graphics graphics, string label, decimal amount, int x, int y, Font font, Brush brush)
    {
        graphics.DrawString(label, font, brush, x, y);
        graphics.DrawString(amount.ToString("C"), font, brush, x + 135, y);
    }
}
