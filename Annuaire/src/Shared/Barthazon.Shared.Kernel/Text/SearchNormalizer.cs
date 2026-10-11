using System.Globalization;
using System.Text;

namespace Barthazon.Shared.Kernel.Text;

/// <summary>
/// Normalisation pour la recherche tolérante : insensible à la casse et aux accents,
/// et ignorant espaces, tirets, points, barres et soulignés dans les références
/// (« AB-123.4 » = « ab1234 »).
/// </summary>
public static class SearchNormalizer
{
    private static readonly char[] Separators = [' ', '-', '.', '/', '_', '\\', ',', ';', ':', '\t', '\r', '\n', '(', ')', '\'', '"', '×', '+'];

    /// <summary>Minuscules, sans accents, espaces multiples réduits.</summary>
    public static string Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var decomposed = text.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        var lastWasSpace = false;
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var mapped = c switch
            {
                'œ' => "oe",
                'æ' => "ae",
                'ß' => "ss",
                'ø' => "o",
                _ => null,
            };

            if (char.IsWhiteSpace(c))
            {
                if (!lastWasSpace)
                {
                    builder.Append(' ');
                }

                lastWasSpace = true;
                continue;
            }

            lastWasSpace = false;
            if (mapped is null)
            {
                builder.Append(c);
            }
            else
            {
                builder.Append(mapped);
            }
        }

        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>Forme compacte : normalisée et sans aucun séparateur (pour les références).</summary>
    public static string Compact(string? text)
    {
        var normalized = Normalize(text);
        if (normalized.Length == 0)
        {
            return normalized;
        }

        var builder = new StringBuilder(normalized.Length);
        foreach (var c in normalized)
        {
            if (Array.IndexOf(Separators, c) < 0)
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    /// <summary>Découpe une saisie en termes de recherche compacts (tous doivent être trouvés).</summary>
    public static IReadOnlyList<string> Terms(string? query)
    {
        var normalized = Normalize(query);
        if (normalized.Length == 0)
        {
            return [];
        }

        return normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(Compact)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Longueur de la colonne de recherche.</summary>
    public const int MaxSearchTextLength = 4000;

    /// <summary>
    /// Construit le texte de recherche d'un objet : pour chaque valeur, la forme normalisée (mots)
    /// et la forme compacte (références sans séparateurs), séparées par « | ».
    /// </summary>
    public static string BuildSearchText(IEnumerable<string?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var parts = new List<string>();
        foreach (var value in values)
        {
            var normalized = Normalize(value);
            if (normalized.Length == 0)
            {
                continue;
            }

            parts.Add(normalized);
            var compact = Compact(value);
            if (compact != normalized)
            {
                parts.Add(compact);
            }
        }

        if (parts.Count == 0)
        {
            return string.Empty;
        }

        // Colonne limitée à MaxSearchTextLength : on garde les premières valeurs (référence, désignation, client…), les plus utiles.
        var builder = new StringBuilder("|");
        foreach (var part in parts.Distinct(StringComparer.Ordinal))
        {
            if (builder.Length + part.Length + 1 > MaxSearchTextLength)
            {
                break;
            }

            builder.Append(part).Append('|');
        }

        return builder.ToString();
    }

    /// <summary>Vrai si tous les termes de la saisie sont présents dans le texte de recherche.</summary>
    public static bool Matches(string searchText, string? query)
    {
        ArgumentNullException.ThrowIfNull(searchText);
        return Terms(query).All(term => searchText.Contains(term, StringComparison.Ordinal));
    }
}
