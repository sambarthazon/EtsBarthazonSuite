using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace Barthazon.Portal.Hosting.Demo;

/// <summary>Fichiers fictifs pour la démonstration : un plan PDF simplifié et une photo PNG.</summary>
internal static class DemoFiles
{
    /// <summary>Plan A4 paysage : cadre, cartouche, arbre étagé avec axe et cotes.</summary>
    public static byte[] DrawingPdf(string reference, string designation, string index, string customer, IReadOnlyList<(double Length, double Diameter)> steps)
    {
        var content = new StringBuilder();
        void Line(string s) => content.Append(s).Append('\n');
        string N(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
        string Text(string s) => s.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("(", "\\(", StringComparison.Ordinal).Replace(")", "\\)", StringComparison.Ordinal);

        Line("1 w 20 20 802 555 re S");
        Line("0.6 w 30 30 782 535 re S");
        Line("1 w 512 30 300 110 re S 512 85 m 812 85 l S 512 60 m 812 60 l S 700 30 m 700 140 l S");

        // Arbre étagé : échelle pour tenir dans 520 points.
        var total = steps.Sum(s => s.Length);
        var maxDiameter = steps.Max(s => s.Diameter);
        var scale = Math.Min(520 / total, 200 / maxDiameter);
        var x = 130.0;
        const double axis = 350;
        Line("1.4 w");
        foreach (var (length, diameter) in steps)
        {
            var w = length * scale;
            var h = diameter * scale;
            Line($"{N(x)} {N(axis - h / 2)} {N(w)} {N(h)} re S");
            Line($"BT /F1 9 Tf {N(x + w / 2 - 14)} {N(axis + h / 2 + 8)} Td (\\330{N(diameter)}) Tj ET");
            Line($"BT /F1 9 Tf {N(x + w / 2 - 10)} {N(axis - maxDiameter * scale / 2 - 22)} Td ({N(length)}) Tj ET");
            x += w;
        }

        Line("0.5 w [12 4 2 4] 0 d 110 350 m " + N(x + 20) + " 350 l S [] 0 d");
        Line($"BT /F1 9 Tf 130 {N(axis - maxDiameter * scale / 2 - 40)} Td (Longueur totale {N(total)}) Tj ET");
        Line($"BT /F1 16 Tf 520 112 Td ({Text(reference)}) Tj ET");
        Line($"BT /F1 10 Tf 520 92 Td ({Text(designation)}) Tj ET");
        Line($"BT /F1 10 Tf 520 68 Td (Client : {Text(customer)}) Tj ET");
        Line($"BT /F1 10 Tf 520 40 Td (Plan de demonstration - fictif) Tj ET");
        Line($"BT /F1 10 Tf 708 112 Td (Indice) Tj ET");
        Line($"BT /F1 28 Tf 735 70 Td ({Text(index)}) Tj ET");
        Line("BT /F1 9 Tf 40 40 Td (Tolerance generale ISO 2768-mK - Cotes en mm - Ne pas mesurer sur le plan) Tj ET");

        var stream = Encoding.Latin1.GetBytes(content.ToString());
        var objects = new List<byte[]>
        {
            Encoding.ASCII.GetBytes("<< /Type /Catalog /Pages 2 0 R >>"),
            Encoding.ASCII.GetBytes("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Encoding.ASCII.GetBytes("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 842 595] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>"),
            Encoding.ASCII.GetBytes($"<< /Length {stream.Length} >>\nstream\n").Concat(stream).Concat(Encoding.ASCII.GetBytes("\nendstream")).ToArray(),
            Encoding.ASCII.GetBytes("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>"),
        };

        using var output = new MemoryStream();
        void Write(string s) => output.Write(Encoding.ASCII.GetBytes(s));
        Write("%PDF-1.4\n");
        var offsets = new List<long>();
        for (var i = 0; i < objects.Count; i++)
        {
            offsets.Add(output.Position);
            Write($"{i + 1} 0 obj\n");
            output.Write(objects[i]);
            Write("\nendobj\n");
        }

        var xref = output.Position;
        Write($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets)
        {
            Write($"{offset:0000000000} 00000 n \n");
        }

        Write($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF\n");
        return output.ToArray();
    }

    /// <summary>« Photo » 320×240 : pièce cylindrique ombrée sur fond neutre.</summary>
    public static byte[] PhotoPng(int hue)
    {
        const int width = 320;
        const int height = 240;
        var raw = new byte[height * (1 + width * 3)];
        for (var y = 0; y < height; y++)
        {
            var row = y * (1 + width * 3);
            raw[row] = 0;
            for (var x = 0; x < width; x++)
            {
                var inPart = x is > 40 and < 280 && Math.Abs(y - 120) < (x < 140 ? 70 : x < 220 ? 50 : 35);
                byte r, g, b;
                if (inPart)
                {
                    var radius = x < 140 ? 70 : x < 220 ? 50 : 35;
                    var shade = 1 - Math.Abs(y - 120) / (double)radius;
                    var level = (byte)(90 + 140 * Math.Sqrt(Math.Max(0, shade)));
                    r = (byte)Math.Min(255, level + (hue == 1 ? 40 : 0));
                    g = (byte)Math.Min(255, level + (hue == 2 ? 30 : 0));
                    b = (byte)Math.Min(255, level + 10);
                }
                else
                {
                    var bg = (byte)(225 - y / 12);
                    (r, g, b) = (bg, bg, (byte)(bg + 5));
                }

                var p = row + 1 + x * 3;
                raw[p] = r;
                raw[p + 1] = g;
                raw[p + 2] = b;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        using var png = new MemoryStream();
        png.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        WriteChunk(png, "IHDR", [0, 0, 1, 64, 0, 0, 0, 240, 8, 2, 0, 0, 0]);
        WriteChunk(png, "IDAT", compressed.ToArray());
        WriteChunk(png, "IEND", []);
        return png.ToArray();
    }

    private static void WriteChunk(Stream stream, string type, byte[] data)
    {
        var length = BitConverter.GetBytes(data.Length);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(length);
        }

        var typeBytes = Encoding.ASCII.GetBytes(type);
        stream.Write(length);
        stream.Write(typeBytes);
        stream.Write(data);
        var crc = Crc32([.. typeBytes, .. data]);
        var crcBytes = BitConverter.GetBytes(crc);
        if (BitConverter.IsLittleEndian)
        {
            Array.Reverse(crcBytes);
        }

        stream.Write(crcBytes);
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var k = 0; k < 8; k++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return crc ^ 0xFFFFFFFFu;
    }
}
