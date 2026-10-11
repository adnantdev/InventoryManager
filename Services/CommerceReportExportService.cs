using System.Reflection;
using System.Text;
using System.Xml;

namespace InventoryManager.Services;

public static class CommerceReportExportService
{
    private const string SpreadsheetNamespace = "urn:schemas-microsoft-com:office:spreadsheet";

    public static void ExportExcel<T>(string path, string title, IReadOnlyList<T> rows)
    {
        var properties = GetProperties<T>();
        var settings = new XmlWriterSettings { Indent = true, Encoding = new UTF8Encoding(true) };
        using var writer = XmlWriter.Create(path, settings);
        writer.WriteStartDocument();
        writer.WriteStartElement("Workbook", SpreadsheetNamespace);
        writer.WriteAttributeString("xmlns", "ss", null, SpreadsheetNamespace);
        writer.WriteStartElement("Worksheet", SpreadsheetNamespace);
        writer.WriteAttributeString("ss", "Name", SpreadsheetNamespace, "Report");
        writer.WriteStartElement("Table", SpreadsheetNamespace);
        WriteExcelRow(writer, new object?[] { title });
        WriteExcelRow(writer, properties.Select(property => (object?)property.Name));
        foreach (var row in rows)
            WriteExcelRow(writer, properties.Select(property => property.GetValue(row)));
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndDocument();
    }

    public static void ExportPdf<T>(string path, string title, IReadOnlyList<T> rows)
    {
        var properties = GetProperties<T>();
        var lines = new List<string>
        {
            title,
            string.Join(" | ", properties.Select(property => property.Name)),
            new string('-', 110)
        };
        lines.AddRange(rows.Select(row => string.Join(" | ", properties.Select(property =>
            Truncate(Format(property.GetValue(row)), 24)))));
        if (rows.Count == 0) lines.Add("No records for this report.");

        const int linesPerPage = 48;
        var pageLines = lines.Chunk(linesPerPage).ToList();
        var objects = new List<string>
        {
            "<< /Type /Catalog /Pages 2 0 R >>",
            "<< /Type /Pages /Kids [" + string.Join(" ", Enumerable.Range(0, pageLines.Count)
                .Select(index => $"{4 + index * 2} 0 R")) + $"] /Count {pageLines.Count} >>",
            "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>"
        };

        for (var page = 0; page < pageLines.Count; page++)
        {
            var content = new StringBuilder();
            content.Append("BT /F1 9 Tf 36 806 Td 12 TL\n");
            foreach (var line in pageLines[page])
                content.Append('(').Append(EscapePdf(Truncate(line, 125))).Append(") Tj T*\n");
            content.Append("ET");
            var stream = content.ToString();
            objects.Add($"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 595 842] /Resources << /Font << /F1 3 0 R >> >> /Contents {5 + page * 2} 0 R >>");
            objects.Add($"<< /Length {Encoding.ASCII.GetByteCount(stream)} >>\nstream\n{stream}\nendstream");
        }

        using var output = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        WriteAscii(output, "%PDF-1.4\n");
        var offsets = new List<long> { 0 };
        for (var index = 0; index < objects.Count; index++)
        {
            offsets.Add(output.Position);
            WriteAscii(output, $"{index + 1} 0 obj\n{objects[index]}\nendobj\n");
        }
        var xrefOffset = output.Position;
        WriteAscii(output, $"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets.Skip(1))
            WriteAscii(output, $"{offset:D10} 00000 n \n");
        WriteAscii(output, $"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xrefOffset}\n%%EOF");
    }

    private static PropertyInfo[] GetProperties<T>() => typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public);

    private static void WriteExcelRow(XmlWriter writer, IEnumerable<object?> cells)
    {
        writer.WriteStartElement("Row", SpreadsheetNamespace);
        foreach (var cell in cells)
        {
            writer.WriteStartElement("Cell", SpreadsheetNamespace);
            writer.WriteStartElement("Data", SpreadsheetNamespace);
            writer.WriteAttributeString("ss", "Type", SpreadsheetNamespace, IsNumeric(cell) ? "Number" : "String");
            writer.WriteString(Format(cell));
            writer.WriteEndElement();
            writer.WriteEndElement();
        }
        writer.WriteEndElement();
    }

    private static string Format(object? value) => value switch
    {
        null => string.Empty,
        DateTime date => date.ToString("yyyy-MM-dd HH:mm"),
        decimal amount => amount.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
        bool boolean => boolean ? "Yes" : "No",
        IFormattable formattable => formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        _ => Convert.ToString(value) ?? string.Empty
    };

    private static bool IsNumeric(object? value) => value is byte or short or int or long or float or double or decimal;

    private static string Truncate(string value, int length) =>
        value.Length <= length ? value : value[..(length - 1)] + "…";

    private static string EscapePdf(string value)
    {
        var ascii = new string(value.Select(character => character is >= ' ' and <= '~' ? character : '?').ToArray());
        return ascii.Replace("\\", "\\\\").Replace("(", "\\(").Replace(")", "\\)");
    }

    private static void WriteAscii(Stream stream, string value)
    {
        var bytes = Encoding.ASCII.GetBytes(value);
        stream.Write(bytes, 0, bytes.Length);
    }
}
