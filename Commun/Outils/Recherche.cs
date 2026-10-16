using System.Globalization;
using System.Text;

namespace Barthazon.Commun.Outils;

/// <summary>
/// Recherche au fil de la frappe, insensible à la casse et aux accents.
/// Chaque mot tapé doit se retrouver dans le texte (« 40 42cr » trouve « Rond Ø40 × 120 — 42CrMo4 »).
/// </summary>
public static class Recherche
{
    /// <summary>Minuscules, sans accents, « Ø » et « × » remplacés par des espaces.</summary>
    public static string Normaliser(string? texte)
    {
        if (string.IsNullOrWhiteSpace(texte))
        {
            return string.Empty;
        }

        var decompose = texte.Normalize(NormalizationForm.FormD);
        var resultat = new StringBuilder(decompose.Length);
        foreach (var c in decompose)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            resultat.Append(c switch
            {
                'Ø' or 'ø' or '×' or '—' or '–' => ' ',
                _ => char.ToLowerInvariant(c),
            });
        }

        return resultat.ToString();
    }

    public static string[] Mots(string? requete) =>
        Normaliser(requete).Split([' ', '\t', ',', ';'], StringSplitOptions.RemoveEmptyEntries);

    /// <summary>Vrai si tous les mots de la requête figurent dans l'un des textes.</summary>
    public static bool Correspond(string? requete, params string?[] textes)
    {
        var mots = Mots(requete);
        if (mots.Length == 0)
        {
            return true;
        }

        var cible = Normaliser(string.Join(' ', textes.Where(t => !string.IsNullOrEmpty(t))));
        return mots.All(m => cible.Contains(m, StringComparison.Ordinal));
    }
}
