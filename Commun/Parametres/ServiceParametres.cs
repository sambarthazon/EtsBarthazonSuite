using System.Globalization;
using System.Text.RegularExpressions;
using Barthazon.Commun.Donnees;
using Barthazon.Commun.Outils;
using Barthazon.Commun.Securite;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Commun.Parametres;

/// <summary>Paramètres enregistrés en base (table com_Parametre).</summary>
public sealed partial class ServiceParametres(IDbContextFactory<CommunDbContext> fabrique, IUtilisateurCourant courant)
{
    public const string CleCouleurPrincipale = "theme.couleur-principale";

    /// <summary>Couleur principale par défaut (orange atelier) ; doit rester identique à theme.css.</summary>
    public const string CouleurParDefaut = "#d9631e";

    private string? _couleurEnCache;

    public async Task<string?> Lire(string cle)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        return (await db.Parametres.AsNoTracking().FirstOrDefaultAsync(p => p.Cle == cle))?.Valeur;
    }

    public async Task Ecrire(string cle, string valeur)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var parametre = await db.Parametres.FirstOrDefaultAsync(p => p.Cle == cle);
        if (parametre is null)
        {
            db.Parametres.Add(new Parametre { Cle = cle, Valeur = valeur });
        }
        else
        {
            parametre.Valeur = valeur;
        }

        await db.SaveChangesAsync();
    }

    public async Task<string> CouleurPrincipale()
    {
        if (_couleurEnCache is not null)
        {
            return _couleurEnCache;
        }

        var valeur = await Lire(CleCouleurPrincipale);
        _couleurEnCache = valeur is not null && CouleurValide(valeur) ? valeur : CouleurParDefaut;
        return _couleurEnCache;
    }

    public async Task<Resultat> DefinirCouleurPrincipale(string couleur)
    {
        if (!courant.Peut(Role.Administrateur))
        {
            return Resultat.Echec(Resultat.NonAutorise);
        }

        if (!CouleurValide(couleur))
        {
            return Resultat.Echec("Couleur invalide.");
        }

        await Ecrire(CleCouleurPrincipale, couleur.ToLowerInvariant());
        _couleurEnCache = couleur.ToLowerInvariant();
        return Resultat.Ok();
    }

    public static bool CouleurValide(string? couleur) => couleur is not null && FormatCouleur().IsMatch(couleur);

    /// <summary>Texte lisible sur la couleur principale : noir sur une couleur claire, blanc sinon.</summary>
    public static string TexteSurCouleur(string couleur)
    {
        if (!CouleurValide(couleur))
        {
            return "#ffffff";
        }

        double Canal(int debut)
        {
            var c = int.Parse(couleur.AsSpan(debut, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var luminance = (0.2126 * Canal(1)) + (0.7152 * Canal(3)) + (0.0722 * Canal(5));
        return luminance > 0.36 ? "#111111" : "#ffffff";
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex FormatCouleur();
}
