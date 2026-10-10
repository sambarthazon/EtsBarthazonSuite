using System.Globalization;

namespace Barthazon.Catalog.UI.Components;

/// <summary>Propose l'indice suivant (A → B, Z → AA, 01 → 02, « - » → A).</summary>
public static class IndexSuggester
{
    public static string Next(IEnumerable<string> existing)
    {
        var all = existing.ToList();
        var last = all.LastOrDefault();
        string candidate = last switch
        {
            null or "" or "-" => "A",
            _ when last.All(char.IsAsciiDigit) => (int.Parse(last, CultureInfo.InvariantCulture) + 1).ToString(new string('0', last.Length), CultureInfo.InvariantCulture),
            _ when last.All(char.IsAsciiLetterUpper) => NextLetters(last),
            _ when last.All(char.IsAsciiLetterLower) => NextLetters(last.ToUpperInvariant()).ToLowerInvariant(),
            _ => string.Empty,
        };
        return all.Contains(candidate, StringComparer.OrdinalIgnoreCase) ? string.Empty : candidate;
    }

    private static string NextLetters(string value)
    {
        var chars = value.ToCharArray();
        for (var i = chars.Length - 1; i >= 0; i--)
        {
            if (chars[i] < 'Z')
            {
                chars[i]++;
                return new string(chars);
            }

            chars[i] = 'A';
        }

        return "A" + new string(chars);
    }
}
