using System.Reflection;
using Barthazon.Catalog.Core;
using Barthazon.Catalog.UI.Components;
using Barthazon.Catalog.UI.Resources;
using Barthazon.Catalog.UI.Services;
using Barthazon.Shared.Kernel.Modules;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Shared.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Barthazon.Catalog.UI;

/// <summary>Module « Annuaire de pièces ».</summary>
public sealed class CatalogModule : IModule
{
    public string Key => "catalog";

    public string DisplayName => CatalogTexts.ModuleName;

    public string Description => CatalogTexts.ModuleDescription;

    public string Icon => Icons.Material.Filled.MenuBook;

    public string EntryRoute => "pieces/accueil";

    public IReadOnlyList<string> RoutePrefixes { get; } = ["/pieces"];

    public ModuleState State => ModuleState.Available;

    public int Order => 10;

    public bool IsApplication => true;

    public IReadOnlyList<ModuleMenuItem> MenuItems { get; } =
    [
        new(CatalogTexts.MenuDashboard, "pieces/accueil", Icons.Material.Filled.Dashboard, MatchAll: true),
        new(CatalogTexts.MenuParts, "pieces", Icons.Material.Filled.ViewList, MatchAll: true),
        new(CatalogTexts.MenuNewPart, "pieces/nouvelle", Icons.Material.Filled.AddCircle, MatchAll: true, MinimumRole: UserRole.Editor, ShowInBottomBar: false),
        new(CatalogTexts.MenuImport, "pieces/import", Icons.Material.Filled.UploadFile, MatchAll: true, MinimumRole: UserRole.Editor, ShowInBottomBar: false),
    ];

    public Assembly? UiAssembly => typeof(CatalogModule).Assembly;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddCatalogCore();
        services.AddScoped<IGlobalSearchProvider, PartGlobalSearchProvider>();
        services.AddScoped<PartQrCodes>();

        // Section « Pièces du client » de la fiche client (module Référentiel).
        services.AddUiExtension(new UiExtension(UiSlots.CustomerDetails, typeof(CustomerPartsSection), CatalogTexts.CustomerPartsTitle, 10));

        // Onglets « Familles » et « Étiquettes » de la page Référentiels.
        services.AddUiExtension(new UiExtension(UiSlots.ReferentialTabs, typeof(FamiliesPanel), CatalogTexts.TabFamilies, 10, Icons.Material.Filled.Category));
        services.AddUiExtension(new UiExtension(UiSlots.ReferentialTabs, typeof(TagsPanel), CatalogTexts.TabTags, 20, Icons.Material.Filled.Sell));
    }
}
