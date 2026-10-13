namespace Barthazon.Shared.UI.Services;

/// <summary>Logo et nom de l'entreprise (configuration « App »).</summary>
public sealed class BrandingOptions
{
    public const string SectionName = "App";

    /// <summary>Nom affiché (peut être remplacé depuis l'administration).</summary>
    public string Name { get; set; } = "Barthazon — Outils atelier";

    public string CompanyName { get; set; } = "Ets BARTHAZON";

    /// <summary>Chemin du logo, relatif à la racine du site.</summary>
    public string LogoUrl { get; set; } = "img/logo.svg";
}

/// <summary>Résultat de la recherche globale (barre du haut).</summary>
public sealed record GlobalSearchHit(string Title, string? Subtitle, string Href, string? Icon = null, string? Badge = null);

/// <summary>Fournisseur de résultats pour la recherche globale, implémenté par chaque module qui le souhaite.</summary>
public interface IGlobalSearchProvider
{
    /// <summary>Ordre d'affichage des résultats entre fournisseurs.</summary>
    int Order { get; }

    Task<IReadOnlyList<GlobalSearchHit>> SearchAsync(string query, int maxResults, CancellationToken cancellationToken);

    /// <summary>Adresse de la page de résultats complets.</summary>
    string FullResultsUrl(string query);
}

/// <summary>
/// Extension d'interface : un module peut insérer un composant dans un « emplacement » d'une page
/// d'un autre module sans que celui-ci le connaisse (ex. la liste des pièces dans la fiche client).
/// </summary>
/// <param name="Slot">Nom de l'emplacement.</param>
/// <param name="ComponentType">Composant à afficher ; il reçoit les paramètres fournis par l'emplacement.</param>
/// <param name="Title">Titre (onglet, section).</param>
/// <param name="Order">Ordre d'affichage.</param>
/// <param name="Icon">Icône éventuelle.</param>
public sealed record UiExtension(string Slot, Type ComponentType, string Title, int Order = 0, string? Icon = null);

/// <summary>Noms des emplacements connus.</summary>
public static class UiSlots
{
    /// <summary>Fiche client : sections supplémentaires. Paramètre : <c>CustomerId</c> (Guid).</summary>
    public const string CustomerDetails = "customer-details";

    /// <summary>Page « Référentiels » : onglets supplémentaires. Aucun paramètre.</summary>
    public const string ReferentialTabs = "referential-tabs";

    /// <summary>Page d'administration : sections supplémentaires. Aucun paramètre.</summary>
    public const string AdminSections = "admin-sections";
}

/// <summary>Registre des extensions d'interface.</summary>
public sealed class UiExtensionRegistry(IEnumerable<UiExtension> extensions)
{
    private readonly List<UiExtension> _extensions = extensions.ToList();

    public IReadOnlyList<UiExtension> For(string slot) =>
        _extensions.Where(e => e.Slot == slot).OrderBy(e => e.Order).ToList();
}
