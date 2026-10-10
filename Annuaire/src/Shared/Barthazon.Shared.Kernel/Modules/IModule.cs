using System.Reflection;
using Barthazon.Shared.Kernel.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Shared.Kernel.Modules;

/// <summary>État d'un module sur le portail.</summary>
public enum ModuleState
{
    Available,
    ComingSoon,
}

/// <summary>Entrée de menu d'un module.</summary>
/// <param name="Title">Libellé affiché.</param>
/// <param name="Href">Lien (relatif).</param>
/// <param name="Icon">Icône (SVG MudBlazor).</param>
/// <param name="MatchAll">Vrai si le lien n'est actif que sur l'adresse exacte.</param>
/// <param name="MinimumRole">Rôle minimum pour afficher l'entrée (null = tout le monde).</param>
/// <param name="ShowInBottomBar">Affichée dans la barre de navigation basse sur téléphone.</param>
public sealed record ModuleMenuItem(
    string Title,
    string Href,
    string Icon,
    bool MatchAll = false,
    UserRole? MinimumRole = null,
    bool ShowInBottomBar = true);

/// <summary>
/// Une « application » du portail (annuaire, stock, commandes…). Chaque module déclare son identité,
/// son menu, ses services et l'assemblage de ses pages Razor. Le portail parcourt la liste des modules
/// pour construire la page d'accueil, le menu et le routage.
/// La partie persistance (mappings EF Core) est déclarée séparément par le projet Infrastructure du module.
/// </summary>
public interface IModule
{
    /// <summary>Clé technique unique (ex. « catalog »).</summary>
    string Key { get; }

    string DisplayName { get; }

    string Description { get; }

    string Icon { get; }

    /// <summary>Route d'entrée (ex. « /pieces/accueil »).</summary>
    string EntryRoute { get; }

    /// <summary>Préfixes d'URL appartenant au module (pour afficher son menu).</summary>
    IReadOnlyList<string> RoutePrefixes { get; }

    ModuleState State { get; }

    /// <summary>Ordre d'affichage des tuiles.</summary>
    int Order { get; }

    /// <summary>
    /// Vrai pour une application affichée en tuile sur le portail (annuaire, stock…) ;
    /// faux pour un module de données partagées (référentiel), dont le menu est commun à toutes les applications.
    /// </summary>
    bool IsApplication { get; }

    IReadOnlyList<ModuleMenuItem> MenuItems { get; }

    /// <summary>Assemblage contenant les pages Razor du module (null pour un module à venir).</summary>
    Assembly? UiAssembly { get; }

    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
}

/// <summary>Module « à venir » : uniquement une tuile grisée sur le portail, aucun code métier.</summary>
public sealed class ComingSoonModule(string key, string displayName, string description, string icon, int order) : IModule
{
    public string Key { get; } = key;

    public string DisplayName { get; } = displayName;

    public string Description { get; } = description;

    public string Icon { get; } = icon;

    public string EntryRoute => string.Empty;

    public IReadOnlyList<string> RoutePrefixes => [];

    public ModuleState State => ModuleState.ComingSoon;

    public int Order { get; } = order;

    public bool IsApplication => true;

    public IReadOnlyList<ModuleMenuItem> MenuItems => [];

    public Assembly? UiAssembly => null;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
