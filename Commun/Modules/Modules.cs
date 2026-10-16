using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;
using Barthazon.Commun.Securite;
using Microsoft.AspNetCore.Routing;

namespace Barthazon.Commun.Modules;

/// <summary>Onglet d'une application (deuxième niveau de navigation).</summary>
/// <param name="Secondaire">Rangé dans le menu « ⋯ ».</param>
/// <param name="Pastille">Nombre affiché en pastille (ex. alertes), ou null.</param>
public sealed record OngletApplication(
    string Titre,
    string Chemin,
    bool Secondaire = false,
    Func<IServiceProvider, Task<int>>? Pastille = null);

/// <summary>Correspondance entre une route et un objet : « /annuaire/pieces/{id} » ↔ (piece-finie, id).</summary>
public sealed record RouteObjet(string TypeObjet, string Modele);

/// <summary>Onglet ajouté par une application à la page Paramètres.</summary>
public sealed record OngletParametres(string Cle, string Titre, Type Composant, Role RoleMinimum = Role.Administrateur);

/// <summary>Description d'une application du portail : chaque module en déclare une.</summary>
public sealed record ModuleApplication
{
    /// <summary>Clé courte, aussi premier segment des adresses : « annuaire », « stock ».</summary>
    public required string Cle { get; init; }

    public required string Nom { get; init; }

    public required Assembly Assemblage { get; init; }

    public int Ordre { get; init; }

    public string CheminAccueil => "/" + Cle;

    public IReadOnlyList<OngletApplication> Onglets { get; init; } = [];

    /// <summary>Fiches de l'application, pour la bascule contextuelle et la recherche globale.</summary>
    public IReadOnlyList<RouteObjet> Routes { get; init; } = [];

    /// <summary>Formulaires de création (« + Nouveau brut » depuis une autre application).</summary>
    public IReadOnlyList<RouteObjet> RoutesCreation { get; init; } = [];

    /// <summary>Nombre d'alertes affiché sur la cloche de la barre du haut.</summary>
    public Func<IServiceProvider, Task<int>>? CompteurAlertes { get; init; }

    public string? CheminAlertes { get; init; }

    public IReadOnlyList<OngletParametres> Parametres { get; init; } = [];

    /// <summary>Feuille de style propre à l'application (chargée par le portail), ex. « _content/Barthazon.Stock/stock.css ».</summary>
    public string? FeuilleDeStyle { get; init; }

    /// <summary>Points d'accès HTTP propres à l'application (fichiers, exports…).</summary>
    public Action<IEndpointRouteBuilder>? PointsAcces { get; init; }
}

/// <summary>Applications chargées dans le portail, dans l'ordre d'affichage.</summary>
public sealed class CatalogueModules(IEnumerable<ModuleApplication> modules)
{
    public IReadOnlyList<ModuleApplication> Modules { get; } = modules.OrderBy(m => m.Ordre).ThenBy(m => m.Nom, StringComparer.Ordinal).ToList();

    public ModuleApplication? Trouver(string? cle) => Modules.FirstOrDefault(m => string.Equals(m.Cle, cle, StringComparison.OrdinalIgnoreCase));

    /// <summary>Application dont relève une adresse (« /stock/bruts/3 » → Stock).</summary>
    public ModuleApplication? DepuisChemin(string? chemin)
    {
        if (string.IsNullOrEmpty(chemin))
        {
            return null;
        }

        var segment = chemin.TrimStart('/').Split('/', '?', '#')[0];
        return Trouver(segment);
    }
}

/// <summary>
/// Bascule contextuelle : depuis la fiche d'une pièce dans une application, l'autre application
/// ouvre la même pièce. Chaque application déclare ses routes ; ce service fait la correspondance.
/// </summary>
public sealed class ServiceBascule(CatalogueModules catalogue)
{
    private const string Jeton = "{id}";

    /// <summary>Adresse à ouvrir dans <paramref name="cible"/> depuis l'adresse courante.</summary>
    public string Cible(string cheminActuel, ModuleApplication cible)
    {
        ArgumentNullException.ThrowIfNull(cible);
        var actuel = catalogue.DepuisChemin(cheminActuel);
        if (actuel is null || actuel.Cle == cible.Cle)
        {
            return cible.CheminAccueil;
        }

        foreach (var route in actuel.Routes)
        {
            var id = Extraire(route.Modele, cheminActuel);
            if (id is not null)
            {
                var correspondante = cible.Routes.FirstOrDefault(r => r.TypeObjet == route.TypeObjet);
                if (correspondante is not null)
                {
                    return Construire(correspondante.Modele, id.Value);
                }
            }
        }

        return cible.CheminAccueil;
    }

    /// <summary>Adresse d'un objet, de préférence dans l'application courante, sinon dans la première qui l'affiche.</summary>
    public string? Lien(string typeObjet, int id, string? cheminActuel)
    {
        var actuel = catalogue.DepuisChemin(cheminActuel);
        var route = actuel?.Routes.FirstOrDefault(r => r.TypeObjet == typeObjet)
            ?? catalogue.Modules.SelectMany(m => m.Routes).FirstOrDefault(r => r.TypeObjet == typeObjet);
        return route is null ? null : Construire(route.Modele, id);
    }

    /// <summary>Le même objet dans une autre application (« → Stock », « → Fiche annuaire »).</summary>
    public (string Chemin, ModuleApplication Module)? Ailleurs(string typeObjet, int id, string cleModuleActuel)
    {
        foreach (var module in catalogue.Modules.Where(m => m.Cle != cleModuleActuel))
        {
            var route = module.Routes.FirstOrDefault(r => r.TypeObjet == typeObjet);
            if (route is not null)
            {
                return (Construire(route.Modele, id), module);
            }
        }

        return null;
    }

    /// <summary>Formulaire de création d'un objet (dans l'application qui le gère), avec retour à l'adresse donnée.</summary>
    public string? LienCreation(string typeObjet, string? retour)
    {
        var route = catalogue.Modules.SelectMany(m => m.RoutesCreation).FirstOrDefault(r => r.TypeObjet == typeObjet);
        if (route is null)
        {
            return null;
        }

        return string.IsNullOrEmpty(retour) ? route.Modele : $"{route.Modele}?retour={Uri.EscapeDataString(retour)}";
    }

    internal static int? Extraire(string modele, string chemin)
    {
        var sansRequete = chemin.Split('?', '#')[0].TrimEnd('/');
        var motif = "^" + Regex.Escape(modele).Replace(Regex.Escape(Jeton), "(\\d+)", StringComparison.Ordinal) + "(/.*)?$";
        var correspondance = Regex.Match(sansRequete, motif, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return correspondance.Success ? int.Parse(correspondance.Groups[1].Value, CultureInfo.InvariantCulture) : null;
    }

    internal static string Construire(string modele, int id) =>
        modele.Replace(Jeton, id.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
}
