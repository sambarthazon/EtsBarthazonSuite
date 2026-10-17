using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Barthazon.Commun.Composants;
using Barthazon.Commun.Donnees;
using Barthazon.Commun.Outils;
using Barthazon.Commun.Securite;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Commun.Parametres;

/// <summary>Couleur réglable (fond, encadrés, texte…), avec sa valeur par défaut (celle de theme.css).</summary>
public sealed record CouleurTheme(string Cle, string Variable, string Libelle, string Aide, string Defaut);

/// <summary>Réglages d'apparence enregistrés (les couleurs absentes gardent la valeur de theme.css).</summary>
public sealed record ReglagesTheme(string Principale, string? Accent, string? Logo, IReadOnlyDictionary<string, string> Couleurs)
{
    public static ReglagesTheme ParDefaut { get; } = new(ServiceTheme.CouleurPrincipaleParDefaut, null, null, new Dictionary<string, string>());

    /// <summary>Couleur enregistrée, ou valeur par défaut.</summary>
    public string Couleur(CouleurTheme couleur)
    {
        ArgumentNullException.ThrowIfNull(couleur);
        return Couleurs.GetValueOrDefault(couleur.Cle) ?? couleur.Defaut;
    }
}

/// <summary>
/// Apparence du portail (Paramètres → Apparence, administrateur) : logo, couleur principale, couleur
/// d'accentuation et autres couleurs. Une seule apparence pour tous les appareils. Enregistré dans
/// com_Parametre (clés « theme. »), logo dans {DossierDonnees}/theme/.
/// </summary>
public sealed partial class ServiceTheme(
    IDbContextFactory<CommunDbContext> fabrique,
    IUtilisateurCourant courant,
    OptionsBarthazon options,
    SignauxApplication signaux)
{
    /// <summary>Signal émis après un changement d'apparence (le logo se met à jour).</summary>
    public const string Signal = "theme";

    /// <summary>Couleur principale par défaut (orange atelier) ; identique à theme.css.</summary>
    public const string CouleurPrincipaleParDefaut = "#d9631e";

    public const long TailleMaxLogo = 1024 * 1024;

    private const string Prefixe = "theme.";
    private const string ClePrincipale = "theme.couleur-principale";
    private const string CleAccent = "theme.accent";
    private const string PrefixeCouleur = "theme.couleur.";
    private const string CleLogo = "theme.logo";

    private static readonly Dictionary<string, string> TypesLogo = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
    };

    /// <summary>Couleurs réglables. Valeurs par défaut identiques à theme.css (vérifié par un test).</summary>
    public static IReadOnlyList<CouleurTheme> Couleurs { get; } =
    [
        new("fond", "--fond", "Fond de la page", "Derrière les encadrés", "#f3f4f6"),
        new("surface", "--surface", "Fond des encadrés", "Cartes, tableaux, fenêtres", "#ffffff"),
        new("texte", "--texte", "Texte", "Titres et texte courant", "#1b2130"),
        new("texte-doux", "--texte-doux", "Texte secondaire", "Dates, aides, sous-titres", "#5a6273"),
        new("barre-haut", "--barre-haut", "Barre du haut", "Bandeau avec le logo et le sélecteur d'application", "#1f2430"),
        new("succes", "--couleur-succes", "État « OK »", "Stock suffisant, messages de réussite", "#2e7d32"),
        new("alerte", "--couleur-alerte", "État « Sous le seuil »", "Alertes de stock", "#c77700"),
        new("danger", "--couleur-danger", "État « Rupture »", "Ruptures, suppressions, erreurs", "#c62828"),
    ];

    public async Task<ReglagesTheme> Lire()
    {
        try
        {
            await using var db = await fabrique.CreateDbContextAsync();
            var valeurs = await db.Parametres.AsNoTracking()
                .Where(p => p.Cle.StartsWith(Prefixe))
                .ToDictionaryAsync(p => p.Cle, p => p.Valeur);
            return Depuis(valeurs);
        }
        catch (Microsoft.Data.Sqlite.SqliteException)
        {
            // Base pas encore créée : apparence par défaut.
            return ReglagesTheme.ParDefaut;
        }
    }

    internal static ReglagesTheme Depuis(IReadOnlyDictionary<string, string> valeurs)
    {
        string? Couleur(string cle) => valeurs.GetValueOrDefault(cle) is { } v && CouleurValide(v) ? v : null;

        var couleurs = Couleurs
            .Select(c => (c.Cle, Valeur: Couleur(PrefixeCouleur + c.Cle)))
            .Where(c => c.Valeur is not null)
            .ToDictionary(c => c.Cle, c => c.Valeur!);
        var logo = valeurs.GetValueOrDefault(CleLogo);
        return new ReglagesTheme(Couleur(ClePrincipale) ?? CouleurPrincipaleParDefaut, Couleur(CleAccent),
            string.IsNullOrWhiteSpace(logo) ? null : logo, couleurs);
    }

    /// <summary>Enregistre les couleurs (principale, accentuation et autres couleurs).</summary>
    public async Task<Resultat> Enregistrer(ReglagesTheme reglages)
    {
        ArgumentNullException.ThrowIfNull(reglages);
        if (!courant.Peut(Role.Administrateur))
        {
            return Resultat.Echec(Resultat.NonAutorise);
        }

        var couleurs = new[] { reglages.Principale, reglages.Accent }
            .Concat(reglages.Couleurs.Values)
            .OfType<string>();
        if (!couleurs.All(CouleurValide))
        {
            return Resultat.Echec("Couleur invalide.");
        }

        var nouvelles = new Dictionary<string, string>
        {
            [ClePrincipale] = reglages.Principale.ToLowerInvariant(),
        };
        if (reglages.Accent is not null)
        {
            nouvelles[CleAccent] = reglages.Accent.ToLowerInvariant();
        }

        foreach (var couleur in Couleurs)
        {
            if (reglages.Couleurs.GetValueOrDefault(couleur.Cle) is { } valeur && !Egales(valeur, couleur.Defaut))
            {
                nouvelles[PrefixeCouleur + couleur.Cle] = valeur.ToLowerInvariant();
            }
        }

        await using var db = await fabrique.CreateDbContextAsync();
        // Toutes les clés « theme. » sont remplacées, sauf le logo (enregistré à part).
        await db.Parametres.Where(p => p.Cle.StartsWith(Prefixe) && p.Cle != CleLogo).ExecuteDeleteAsync();
        db.Parametres.AddRange(nouvelles.Select(n => new Parametre { Cle = n.Key, Valeur = n.Value }));
        await db.SaveChangesAsync();
        signaux.Emettre(Signal);
        return Resultat.Ok();
    }

    public async Task<Resultat> EnregistrerLogo(string nomFichier, Stream flux, long taille)
    {
        ArgumentNullException.ThrowIfNull(flux);
        if (!courant.Peut(Role.Administrateur))
        {
            return Resultat.Echec(Resultat.NonAutorise);
        }

        var extension = Path.GetExtension(nomFichier ?? string.Empty).ToLowerInvariant();
        if (!TypesLogo.ContainsKey(extension))
        {
            return Resultat.Echec("Le logo doit être une image PNG, JPG, WebP ou SVG.");
        }

        if (taille > TailleMaxLogo)
        {
            return Resultat.Echec("Le logo ne doit pas dépasser 1 Mo.");
        }

        var dossier = Path.Combine(options.CheminDonnees, "theme");
        Directory.CreateDirectory(dossier);
        // Nom horodaté : les navigateurs ne gardent pas l'ancien logo en cache.
        var nom = $"logo-{DateTime.UtcNow.ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture)}{extension}";
        await using (var sortie = new FileStream(Path.Combine(dossier, nom), FileMode.CreateNew, FileAccess.Write))
        {
            await flux.CopyToAsync(sortie);
        }

        var ancien = await RemplacerLogo(nom);
        SupprimerFichier(ancien);
        signaux.Emettre(Signal);
        return Resultat.Ok();
    }

    /// <summary>Retour au logo d'origine.</summary>
    public async Task<Resultat> SupprimerLogo()
    {
        if (!courant.Peut(Role.Administrateur))
        {
            return Resultat.Echec(Resultat.NonAutorise);
        }

        SupprimerFichier(await RemplacerLogo(null));
        signaux.Emettre(Signal);
        return Resultat.Ok();
    }

    /// <summary>Fichier du logo personnalisé (servi par /theme/logo/{nom}).</summary>
    public (string Chemin, string Type)? FichierLogo(string nom)
    {
        if (string.IsNullOrEmpty(nom) || nom != Path.GetFileName(nom) || !TypesLogo.TryGetValue(Path.GetExtension(nom), out var type))
        {
            return null;
        }

        var chemin = Path.Combine(options.CheminDonnees, "theme", nom);
        return File.Exists(chemin) ? (chemin, type) : null;
    }

    /// <summary>CSS injecté dans chaque page : remplace les valeurs de theme.css par les réglages enregistrés.</summary>
    public static string Css(ReglagesTheme reglages)
    {
        ArgumentNullException.ThrowIfNull(reglages);
        var css = new StringBuilder();
        css.Append(":root{");
        Variable(css, "--couleur-principale", reglages.Principale);
        Variable(css, "--texte-sur-principale", TexteSurCouleur(reglages.Principale));
        if (reglages.Accent is not null)
        {
            Variable(css, "--couleur-accent", reglages.Accent);
            Variable(css, "--texte-sur-accent", TexteSurCouleur(reglages.Accent));
        }

        AjouterCouleurs(css, reglages.Couleurs);
        css.Append('}');
        return css.ToString();

        static void AjouterCouleurs(StringBuilder css, IReadOnlyDictionary<string, string> valeurs)
        {
            foreach (var couleur in Couleurs)
            {
                if (valeurs.GetValueOrDefault(couleur.Cle) is { } valeur && CouleurValide(valeur))
                {
                    Variable(css, couleur.Variable, valeur);
                    if (couleur.Cle == "barre-haut")
                    {
                        Variable(css, "--texte-barre-haut", TexteSurCouleur(valeur));
                    }
                }
            }
        }

        static void Variable(StringBuilder css, string nom, string valeur) => css.Append(nom).Append(':').Append(valeur).Append(';');
    }

    public static bool CouleurValide(string? couleur) => couleur is not null && FormatCouleur().IsMatch(couleur);

    /// <summary>Texte lisible sur une couleur : noir sur une couleur claire, blanc sinon.</summary>
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

    private static bool Egales(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Remplace le nom du logo enregistré et renvoie l'ancien.</summary>
    private async Task<string?> RemplacerLogo(string? nom)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var parametre = await db.Parametres.FirstOrDefaultAsync(p => p.Cle == CleLogo);
        var ancien = parametre?.Valeur;
        if (nom is null)
        {
            if (parametre is not null)
            {
                db.Parametres.Remove(parametre);
            }
        }
        else if (parametre is null)
        {
            db.Parametres.Add(new Parametre { Cle = CleLogo, Valeur = nom });
        }
        else
        {
            parametre.Valeur = nom;
        }

        await db.SaveChangesAsync();
        return ancien;
    }

    private void SupprimerFichier(string? nom)
    {
        if (FichierLogo(nom ?? string.Empty) is { } fichier)
        {
            File.Delete(fichier.Chemin);
        }
    }

    [GeneratedRegex("^#[0-9a-fA-F]{6}$")]
    private static partial Regex FormatCouleur();
}
