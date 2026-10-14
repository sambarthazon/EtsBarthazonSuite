using Barthazon.Shared.Kernel.Text;
using Barthazon.Stock.Core.Services;
using ClosedXML.Excel;

namespace Barthazon.Stock.Infrastructure.Exchange;

/// <summary>Classeurs Excel du stock (ClosedXML) : import du stock initial (plusieurs feuilles) et exports.</summary>
internal sealed class ClosedXmlStockSpreadsheet : IStockSpreadsheet
{
    public IReadOnlyList<SheetContent> Read(Stream content, string fileName, IReadOnlyDictionary<string, IReadOnlyList<string>> sheets)
    {
        ArgumentNullException.ThrowIfNull(content);
        ArgumentNullException.ThrowIfNull(sheets);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is not (".xlsx" or ".xlsm"))
        {
            throw new NotSupportedException(extension);
        }

        try
        {
            using var workbook = new XLWorkbook(content);
            var expected = sheets.ToDictionary(s => SearchNormalizer.Compact(s.Key), s => s, StringComparer.Ordinal);
            var result = new List<SheetContent>();
            foreach (var sheet in workbook.Worksheets)
            {
                if (!expected.TryGetValue(SearchNormalizer.Compact(sheet.Name), out var definition))
                {
                    continue;
                }

                result.Add(ReadSheet(sheet, definition.Key, definition.Value));
            }

            return result;
        }
        catch (Exception ex) when (ex is not NotSupportedException and not OutOfMemoryException)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
    }

    public byte[] Write(IReadOnlyList<SheetData> sheets)
    {
        ArgumentNullException.ThrowIfNull(sheets);
        using var workbook = new XLWorkbook();
        foreach (var data in sheets)
        {
            WriteSheet(workbook.Worksheets.Add(data.Name), data);
        }

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private static SheetContent ReadSheet(IXLWorksheet sheet, string name, IReadOnlyList<string> knownColumns)
    {
        var range = sheet.RangeUsed();
        if (range is null)
        {
            return new SheetContent(name, [], []);
        }

        var table = range.Rows().Select(row => row.Cells().Select(CellText).ToList()).ToList();
        var known = knownColumns.ToDictionary(SearchNormalizer.Compact, c => c, StringComparer.Ordinal);
        var headers = table[0].Select(h => known.GetValueOrDefault(SearchNormalizer.Compact(h).TrimEnd('*'))).ToList();
        var firstRow = range.FirstRow().RowNumber();
        var rows = new List<SheetRow>();
        for (var i = 1; i < table.Count; i++)
        {
            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var c = 0; c < headers.Count && c < table[i].Count; c++)
            {
                if (headers[c] is { } column && !string.IsNullOrWhiteSpace(table[i][c]))
                {
                    values.TryAdd(column, table[i][c].Trim());
                }
            }

            if (values.Count > 0)
            {
                rows.Add(new SheetRow(firstRow + i, values));
            }
        }

        return new SheetContent(name, headers.OfType<string>().Distinct().ToList(), rows);
    }

    /// <summary>Texte d'une cellule ; un nombre est rendu sans mise en forme régionale (« 1.5 »), relu par l'import.</summary>
    private static string CellText(IXLCell cell) => cell.DataType == XLDataType.Number
        ? cell.GetDouble().ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
        : cell.GetFormattedString();

    private static void WriteSheet(IXLWorksheet sheet, SheetData data)
    {
        var columns = data.Columns;
        for (var c = 0; c < columns.Count; c++)
        {
            sheet.Cell(1, c + 1).Value = columns[c];
        }

        sheet.Range(1, 1, 1, columns.Count).Style.Font.Bold = true;
        var r = 2;
        foreach (var row in data.Rows)
        {
            for (var c = 0; c < row.Count && c < columns.Count; c++)
            {
                var cell = sheet.Cell(r, c + 1);
                switch (row[c])
                {
                    case null:
                        break;
                    case DateTime date:
                        cell.Value = date;
                        cell.Style.DateFormat.Format = date.TimeOfDay == TimeSpan.Zero ? "dd/mm/yyyy" : "dd/mm/yyyy hh:mm";
                        break;
                    case decimal number:
                        cell.Value = number;
                        break;
                    case int number:
                        cell.Value = number;
                        break;
                    default:
                        // Toujours du texte : « 01 », « A-0001 » ou « 1.0503 » ne doivent pas devenir des nombres.
                        cell.SetValue(row[c]!.ToString());
                        cell.Style.NumberFormat.Format = "@";
                        break;
                }
            }

            r++;
        }

        sheet.Range(1, 1, Math.Max(1, r - 1), columns.Count).SetAutoFilter();
        sheet.SheetView.FreezeRows(1);
        for (var c = 1; c <= columns.Count; c++)
        {
            sheet.Column(c).Width = Math.Clamp(columns[c - 1].Length + 6, 12, 40);
        }
    }
}
