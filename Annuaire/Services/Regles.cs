using System.Globalization;
using System.Text;
using Barthazon.Annuaire.Donnees;
using Barthazon.Commun.Contrats;
using Barthazon.Commun.Outils;

namespace Barthazon.Annuaire.Services;

/// <summary>Libellé automatique d'un brut : « Rond Ø40 × 120 — 42CrMo4 ».</summary>
public static class LibelleBrut
{
    public static string Calculer(FormeBrut forme, decimal? diametre, decimal? diametreInterieur, decimal? largeur, decimal? epaisseur,
        decimal? cote, decimal? surPlats, decimal? longueur, string? description, string? matiere)
    {
        var l = longueur is > 0 ? $" × {Format.Nombre(longueur.Value)}" : string.Empty;
        var texteForme = forme switch
        {
            FormeBrut.RondPlein => $"Rond Ø{Format.Nombre(diametre)}{l}",
            FormeBrut.Tube => $"Tube Ø{Format.Nombre(diametre)} / Ø{Format.Nombre(diametreInterieur)}{l}",
            FormeBrut.Plat => $"Plat {Format.Nombre(largeur)} × {Format.Nombre(epaisseur)}{l}",
            FormeBrut.Carre => $"Carré {Format.Nombre(cote)}{l}",
            FormeBrut.Hexagone => $"Hexagone {Format.Nombre(surPlats)}{l}",
            FormeBrut.Tole => $"Tôle ép. {Format.Nombre(epaisseur)}{(largeur is > 0 ? " × " + Format.Nombre(largeur.Value) : string.Empty)}{l}",
            FormeBrut.MouleeForgee => $"Moulé / forgé : {description}",
            _ => description ?? "Brut",
        };
        return string.IsNullOrWhiteSpace(matiere) ? texteForme : $"{texteForme} — {matiere}";
    }

    public static string Calculer(Brut brut)
    {
        ArgumentNullException.ThrowIfNull(brut);
        return Calculer(brut.Forme, brut.Diametre, brut.DiametreInterieur, brut.Largeur, brut.Epaisseur, brut.Cote, brut.SurPlats,
            brut.Longueur, brut.DescriptionForme, brut.Matiere?.Libelle);
    }

    /// <summary>Libellé affiché : la désignation libre si elle existe, sinon le libellé automatique.</summary>
    public static string Affiche(Brut brut)
    {
        ArgumentNullException.ThrowIfNull(brut);
        return string.IsNullOrWhiteSpace(brut.Designation) ? Calculer(brut) : brut.Designation!;
    }

    /// <summary>Dimensions demandées selon la forme (en mm). Vide si la forme se décrit librement.</summary>
    public static string? Verifier(FormeBrut forme, decimal? diametre, decimal? diametreInterieur, decimal? largeur, decimal? epaisseur,
        decimal? cote, decimal? surPlats, decimal? longueur, string? description)
    {
        static bool Positif(decimal? v) => v is > 0;
        if (longueur is not null && longueur <= 0)
        {
            return "La longueur doit être positive (ou laissée vide pour une barre entière).";
        }

        return forme switch
        {
            FormeBrut.RondPlein when !Positif(diametre) => "Indiquez le diamètre.",
            FormeBrut.Tube when !Positif(diametre) || !Positif(diametreInterieur) => "Indiquez le Ø extérieur et le Ø intérieur.",
            FormeBrut.Tube when diametreInterieur >= diametre => "Le Ø intérieur doit être plus petit que le Ø extérieur.",
            FormeBrut.Plat when !Positif(largeur) || !Positif(epaisseur) => "Indiquez la largeur et l'épaisseur.",
            FormeBrut.Carre when !Positif(cote) => "Indiquez le côté.",
            FormeBrut.Hexagone when !Positif(surPlats) => "Indiquez la cote sur plats.",
            FormeBrut.Tole when !Positif(epaisseur) => "Indiquez l'épaisseur.",
            FormeBrut.MouleeForgee or FormeBrut.Autre when string.IsNullOrWhiteSpace(description) => "Décrivez le brut.",
            _ => null,
        };
    }

    public static string Reference(int numero) => $"BR-{numero.ToString("0000", CultureInfo.InvariantCulture)}";

    public static int Numero(string reference) =>
        reference.StartsWith("BR-", StringComparison.OrdinalIgnoreCase) && int.TryParse(reference.AsSpan(3), NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0;
}

/// <summary>Indices de plan : A → B, Z → AA, 1 → 2, B2 → B3.</summary>
public static class Indices
{
    public static string Suivant(string? code)
    {
        var c = (code ?? string.Empty).Trim();
        if (c.Length == 0)
        {
            return "A";
        }

        // Partie finale à incrémenter : chiffres, ou lettres.
        var fin = c.Length;
        var debut = fin;
        var chiffres = char.IsDigit(c[fin - 1]);
        while (debut > 0 && (chiffres ? char.IsDigit(c[debut - 1]) : char.IsAsciiLetter(c[debut - 1])))
        {
            debut--;
        }

        if (debut == fin)
        {
            return c + "A";
        }

        var prefixe = c[..debut];
        var partie = c[debut..];
        if (chiffres)
        {
            var nombre = long.Parse(partie, CultureInfo.InvariantCulture) + 1;
            return prefixe + nombre.ToString(CultureInfo.InvariantCulture).PadLeft(partie.Length, '0');
        }

        return prefixe + Lettres(partie);
    }

    /// <summary>Incrément en base 26 : A → B, Z → AA, AZ → BA (casse conservée).</summary>
    private static string Lettres(string lettres)
    {
        var minuscule = char.IsLower(lettres[^1]);
        var resultat = new StringBuilder(lettres.ToUpperInvariant());
        var i = resultat.Length - 1;
        while (i >= 0)
        {
            if (resultat[i] != 'Z')
            {
                resultat[i]++;
                break;
            }

            resultat[i] = 'A';
            i--;
        }

        if (i < 0)
        {
            resultat.Insert(0, 'A');
        }

        var texte = resultat.ToString();
        return minuscule ? texte.ToLowerInvariant() : texte;
    }
}

public static class LibellesLien
{
    public static string Libelle(TypeLien type) => type switch
    {
        TypeLien.Remplace => "Remplace",
        TypeLien.RemplaceePar => "Remplacée par",
        TypeLien.MemeFamille => "Même famille",
        TypeLien.SeMonteAvec => "Se monte avec",
        TypeLien.Externe => "Lien externe",
        _ => type.ToString(),
    };

    /// <summary>Type vu depuis la pièce cible (lien réciproque).</summary>
    public static TypeLien Reciproque(TypeLien type) => type switch
    {
        TypeLien.Remplace => TypeLien.RemplaceePar,
        TypeLien.RemplaceePar => TypeLien.Remplace,
        _ => type,
    };
}
