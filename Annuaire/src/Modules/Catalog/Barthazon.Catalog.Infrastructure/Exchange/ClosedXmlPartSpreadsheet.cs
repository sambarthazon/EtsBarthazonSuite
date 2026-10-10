using System.Text;
using Barthazon.Catalog.Core.Services;
using Barthazon.Shared.Kernel.Text;
using ClosedXML.Excel;

namespace Barthazon.Catalog.Infrastructure.Exchange;

/// <summary>Fichiers Excel (ClosedXML) et CSV pour l'import / export des pièces.</summary>
internal sealed class ClosedXmlPartSpreadsheet : IPartSpreadsheet
{
    /// <summary>Autres intitulés acceptés pour les colonnes (forme compacte).</summary>
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["reference"] = ExchangeColumns.InternalReference,
        ["ref"] = ExchangeColumns.InternalReference,
        ["refinterne"] = ExchangeColumns.InternalReference,
        ["client"] = ExchangeColumns.CustomerCode,
        ["refclient"] = ExchangeColumns.CustomerReference,
        ["numerodeplan"] = ExchangeColumns.CustomerReference,
        ["indice"] = ExchangeColumns.Index,
        ["matiere"] = ExchangeColumns.Material,
        ["nuance"] = ExchangeColumns.Grade,
        ["brut"] = ExchangeColumns.BlankDimensions,
        ["debit"] = ExchangeColumns.BlankDimensions,
        ["etiquette"] = ExchangeColumns.Tags,
        ["consignes"] = ExchangeColumns.WorkshopNotes,
        ["tolerance"] = ExchangeColumns.GeneralTolerance,
    };

    public SpreadsheetContent Read(Stream content, string fileName, IReadOnlyList<string> knownColumns)
    {
        ArgumentNullException.ThrowIfNull(content);
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        var table = extension switch
        {
            ".xlsx" or ".xlsm" => ReadExcel(content),
            ".csv" or ".txt" => ReadCsv(content),
            _ => throw new NotSupportedException(extension),
        };

        if (table.Count == 0)
        {
            return new SpreadsheetContent([], []);
        }

        var known = knownColumns.ToDictionary(SearchNormalizer.Compact, c => c, StringComparer.Ordinal);
        var headers = table[0].Select(h =>
        {
            var compact = SearchNormalizer.Compact(h).TrimEnd('*');
            return known.TryGetValue(compact, out var column) ? column : Aliases.GetValueOrDefault(compact);
        }).ToList();

        var rows = new List<SpreadsheetRow>();
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
                rows.Add(new SpreadsheetRow(i + 1, values));
            }
        }

        return new SpreadsheetContent(headers.OfType<string>().Distinct().ToList(), rows);
    }

    public byte[] Write(string sheetName, IReadOnlyList<string> columns, IEnumerable<IReadOnlyList<object?>> rows)
    {
        ArgumentNullException.ThrowIfNull(columns);
        ArgumentNullException.ThrowIfNull(rows);
        using var workbook = new XLWorkbook();
        var sheet = workbook.Worksheets.Add(sheetName);
        for (var c = 0; c < columns.Count; c++)
        {
            sheet.Cell(1, c + 1).Value = columns[c];
        }

        var header = sheet.Range(1, 1, 1, columns.Count);
        header.Style.Font.Bold = true;
        var r = 2;
        foreach (var row in rows)
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
                        cell.Style.DateFormat.Format = "dd/mm/yyyy";
                        break;
                    case decimal number:
                        cell.Value = number;
                        break;
                    case int number:
                        cell.Value = number;
                        break;
                    default:
                        // Toujours du texte : « 01 », « 1.0503 » ou « P-0001 » ne doivent pas devenir des nombres.
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
            sheet.Column(c).Width = Math.Clamp(columns[c - 1].Length + 6, 14, 40);
        }

        using var output = new MemoryStream();
        workbook.SaveAs(output);
        return output.ToArray();
    }

    private static List<List<string>> ReadExcel(Stream content)
    {
        try
        {
            using var workbook = new XLWorkbook(content);
            var sheet = workbook.Worksheets.FirstOrDefault();
            var range = sheet?.RangeUsed();
            if (range is null)
            {
                return [];
            }

            var table = new List<List<string>>();
            foreach (var row in range.Rows())
            {
                table.Add(row.Cells().Select(cell => cell.GetFormattedString()).ToList());
            }

            return table;
        }
        catch (Exception ex) when (ex is not NotSupportedException and not OutOfMemoryException)
        {
            throw new InvalidDataException(ex.Message, ex);
        }
    }

    /// <summary>CSV : séparateur « ; », « , » ou tabulation détecté sur la première ligne ; guillemets gérés.</summary>
    private static List<List<string>> ReadCsv(Stream content)
    {
        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = reader.ReadToEnd();
        if (text.Contains('�', StringComparison.Ordinal))
        {
            // Fichier enregistré par Excel en Windows-1252 : relecture en Latin-1 (accents conservés).
            content.Position = 0;
            using var latin = new StreamReader(content, Encoding.Latin1);
            text = latin.ReadToEnd();
        }

        var firstLine = text.Split('\n', 2)[0];
        var separator = new[] { ';', '\t', ',' }.OrderByDescending(s => firstLine.Count(c => c == s)).First();
        var table = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (quoted)
            {
                if (c == '"' && i + 1 < text.Length && text[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    quoted = false;
                }
                else
                {
                    field.Append(c);
                }
            }
            else if (c == '"')
            {
                quoted = true;
            }
            else if (c == separator)
            {
                row.Add(field.ToString());
                field.Clear();
            }
            else if (c == '\n')
            {
                row.Add(field.ToString().TrimEnd('\r'));
                field.Clear();
                table.Add(row);
                row = [];
            }
            else
            {
                field.Append(c);
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString().TrimEnd('\r'));
            table.Add(row);
        }

        return table.Where(r => r.Any(v => !string.IsNullOrWhiteSpace(v))).ToList();
    }
}
