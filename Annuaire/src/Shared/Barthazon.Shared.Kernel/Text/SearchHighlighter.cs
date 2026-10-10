using System.Globalization;
using System.Text;

namespace Barthazon.Shared.Kernel.Text;

/// <summary>Segment de texte, surligné ou non.</summary>
public readonly record struct HighlightSegment(string Text, bool IsMatch);

/// <summary>
/// Repère dans un texte d'origine les passages correspondant à une saisie de recherche,
/// avec la même tolérance que la recherche (casse, accents, séparateurs).
/// </summary>
public static class SearchHighlighter
{
    public static IReadOnlyList<HighlightSegment> Split(string? text, string? query)
    {
        if (string.IsNullOrEmpty(text))
        {
            return [];
        }

        var terms = SearchNormalizer.Terms(query);
        if (terms.Count == 0)
        {
            return [new HighlightSegment(text, false)];
        }

        // Texte « replié » (compact, sans accents, minuscules) + correspondance vers les indices d'origine.
        var folded = new StringBuilder(text.Length);
        var map = new List<int>(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var piece = SearchNormalizer.Compact(text[i].ToString(CultureInfo.InvariantCulture));
            foreach (var c in piece)
            {
                folded.Append(c);
                map.Add(i);
            }
        }

        var marks = new bool[text.Length];
        var foldedText = folded.ToString();
        foreach (var term in terms)
        {
            var start = 0;
            while (start <= foldedText.Length - term.Length)
            {
                var index = foldedText.IndexOf(term, start, StringComparison.Ordinal);
                if (index < 0)
                {
                    break;
                }

                for (var k = map[index]; k <= map[index + term.Length - 1]; k++)
                {
                    marks[k] = true;
                }

                start = index + term.Length;
            }
        }

        var segments = new List<HighlightSegment>();
        var current = new StringBuilder();
        var currentMark = marks[0];
        for (var i = 0; i < text.Length; i++)
        {
            if (marks[i] != currentMark)
            {
                segments.Add(new HighlightSegment(current.ToString(), currentMark));
                current.Clear();
                currentMark = marks[i];
            }

            current.Append(text[i]);
        }

        segments.Add(new HighlightSegment(current.ToString(), currentMark));
        return segments;
    }
}
