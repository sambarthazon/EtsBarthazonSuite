using System.Text.RegularExpressions;

namespace Barthazon.Architecture.Tests;

/// <summary>Charte : aucune couleur écrite en dur en dehors de Theme/BrandTokens.cs (section 12.2).</summary>
public partial class ThemeRulesTests
{
    [Fact]
    public void No_hard_coded_colors_outside_brand_tokens()
    {
        var offenders = new List<string>();
        var src = Path.Combine(SolutionProjects.Root, "src");
        foreach (var file in Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories))
        {
            var normalized = file.Replace('\\', '/');
            if (normalized.Contains("/bin/", StringComparison.Ordinal) || normalized.Contains("/obj/", StringComparison.Ordinal)
                || normalized.Contains("/Migrations/", StringComparison.Ordinal) || normalized.Contains("/lib/", StringComparison.Ordinal)
                || normalized.EndsWith("Theme/BrandTokens.cs", StringComparison.Ordinal))
            {
                continue;
            }

            var extension = Path.GetExtension(file);
            if (extension is not (".razor" or ".css" or ".cs" or ".js"))
            {
                continue;
            }

            var lineNumber = 0;
            foreach (var line in File.ReadLines(file))
            {
                lineNumber++;
                if (ColorPattern().IsMatch(line))
                {
                    offenders.Add($"{Path.GetRelativePath(SolutionProjects.Root, file)}:{lineNumber}: {line.Trim()}");
                }
            }
        }

        offenders.Should().BeEmpty("toutes les couleurs doivent venir de BrandTokens.cs (variables --bz-*)");
    }

    [GeneratedRegex(@"(#[0-9a-fA-F]{6}\b|#[0-9a-fA-F]{3}\b|\brgba?\s*\(|\bhsla?\s*\(|\b(?:color|background|background-color|fill|stroke|border-color)\s*:\s*(?:white|black|red|blue|green|gray|grey|orange|yellow)\b)")]
    private static partial Regex ColorPattern();
}
