using System.Globalization;
using System.Text.RegularExpressions;

namespace Barthazon.Catalog.Core.Services;

/// <summary>Proposition de référence interne selon le modèle configurable (ex. P-{0:0000} → P-0042).</summary>
public static class ReferenceSuggester
{
    public static string Suggest(string pattern, IEnumerable<string> existingReferences)
    {
        ArgumentNullException.ThrowIfNull(pattern);
        ArgumentNullException.ThrowIfNull(existingReferences);
        var start = pattern.IndexOf("{0", StringComparison.Ordinal);
        var end = start < 0 ? -1 : pattern.IndexOf('}', start);
        if (start < 0 || end < 0)
        {
            return string.Empty;
        }

        var prefix = pattern[..start];
        var suffix = pattern[(end + 1)..];
        var regex = new Regex("^" + Regex.Escape(prefix) + @"(\d+)" + Regex.Escape(suffix) + "$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
        var existing = existingReferences.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var max = existing
            .Select(r => regex.Match(r))
            .Where(m => m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out _))
            .Select(m => int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture))
            .DefaultIfEmpty(0)
            .Max();

        var next = max + 1;
        string candidate;
        do
        {
            candidate = string.Format(CultureInfo.InvariantCulture, pattern, next++);
        }
        while (existing.Contains(candidate));

        return candidate;
    }
}
