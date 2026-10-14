using System.Reflection;
using Barthazon.Catalog.Contracts;
using Barthazon.Shared.Kernel.Modules;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Shared.UI.Services;
using Barthazon.Stock.Core;
using Barthazon.Stock.UI.Components;
using Barthazon.Stock.UI.Resources;
using Barthazon.Stock.UI.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Barthazon.Stock.UI;

/// <summary>
/// Module « Stock ». Le portail ne l'enregistre que si la fonction <c>Stock</c> est active ; sinon une tuile grisée le
/// remplace. La persistance (<c>StockPersistence</c>) est, elle, toujours enregistrée.
/// </summary>
public sealed class StockModule : IModule
{
    public string Key => "stock";

    public string DisplayName => StockTexts.ModuleName;

    public string Description => StockTexts.ModuleDescription;

    public string Icon => Icons.Material.Filled.Inventory;

    public string EntryRoute => "stock";

    public IReadOnlyList<string> RoutePrefixes { get; } = ["/stock"];

    public ModuleState State => ModuleState.Available;

    public int Order => 20;

    public bool IsApplication => true;

    public IReadOnlyList<ModuleMenuItem> MenuItems { get; } =
    [
        new(StockTexts.MenuDashboard, "stock", Icons.Material.Filled.Dashboard, MatchAll: true),
        new(StockTexts.MenuQuickEntry, "stock/saisie", Icons.Material.Filled.QrCodeScanner, MatchAll: true),
        new(StockTexts.MenuBatches, "stock/lancements", Icons.Material.Filled.PrecisionManufacturing),
        new(StockTexts.MenuParts, "stock/pieces", Icons.Material.Filled.Widgets),
        new(StockTexts.MenuItems, "stock/articles", Icons.Material.Filled.Category, ShowInBottomBar: false),
        new(StockTexts.MenuMovements, "stock/mouvements", Icons.Material.Filled.SwapHoriz, ShowInBottomBar: false),
        new(StockTexts.MenuInventories, "stock/inventaires", Icons.Material.Filled.FactCheck, MinimumRole: UserRole.Editor, ShowInBottomBar: false),
    ];

    public Assembly? UiAssembly => typeof(StockModule).Assembly;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddStockCore();

        services.AddScoped<IGlobalSearchProvider, StockGlobalSearchProvider>();
        services.AddScoped<StockQrCodes>();

        // Emplacements de l'annuaire : onglet Stock de la fiche pièce et cartes du tableau de bord.
        services.AddUiExtension(new UiExtension(CatalogUiSlots.PartStockTab, typeof(PartStockPanel), StockTexts.ModuleName, 10));
        services.AddUiExtension(new UiExtension(CatalogUiSlots.DashboardCards, typeof(StockAlertsCard), StockTexts.PartsBelowThreshold, 10));
        services.AddUiExtension(new UiExtension(CatalogUiSlots.DashboardCards, typeof(StockWorkInProgressCard), StockTexts.WorkInProgress, 20));

        // Section « Articles fournis par le client » de la fiche client.
        services.AddUiExtension(new UiExtension(UiSlots.CustomerDetails, typeof(CustomerItemsSection), StockTexts.CustomerItemsTitle, 20));

        // Onglet « Emplacements » de la page Référentiels.
        services.AddUiExtension(new UiExtension(UiSlots.ReferentialTabs, typeof(LocationsPanel), StockTexts.TabLocations, 30, Icons.Material.Filled.Place));
    }
}
