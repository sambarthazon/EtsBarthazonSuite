using Barthazon.Catalog.Infrastructure;
using Barthazon.Catalog.UI;
using Barthazon.Portal.Resources;
using Barthazon.Referential.Infrastructure;
using Barthazon.Referential.UI;
using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Kernel.Features;
using Barthazon.Shared.Kernel.Modules;
using Barthazon.Stock.Infrastructure;
using Barthazon.Stock.UI;
using Microsoft.Extensions.Configuration;
using MudBlazor;

namespace Barthazon.Portal.Hosting;

/// <summary>
/// Liste des modules hébergés par le portail. Pour ajouter une application : créer son module
/// (Contracts / Core / Infrastructure / UI) et l'ajouter ici.
/// </summary>
internal static class PortalModules
{
    /// <summary>
    /// Modules actifs selon la configuration : une application désactivée (<c>Features:Stock = false</c>) est remplacée
    /// par sa tuile grisée et aucun de ses services n'est enregistré.
    /// </summary>
    public static IReadOnlyList<IModule> Create(IConfiguration configuration) =>
    [
        new ReferentialModule(),
        new CatalogModule(),
        IsEnabled(configuration, FeatureNames.Stock)
            ? new StockModule()
            : new ComingSoonModule("stock", PortalTexts.StockName, PortalTexts.StockDescription, Icons.Material.Filled.Inventory, 20),

        // Applications à venir : tuiles grisées, aucun code métier.
        new ComingSoonModule("orders", PortalTexts.OrdersName, PortalTexts.OrdersDescription, Icons.Material.Filled.LocalShipping, 30),
        new ComingSoonModule("time-tracking", PortalTexts.TimeTrackingName, PortalTexts.TimeTrackingDescription, Icons.Material.Filled.Schedule, 40),
    ];

    /// <summary>
    /// Persistance de chaque application (contexte, migrations, repositories), qu'elle soit activée ou non : les tables
    /// existent toujours, activer une application plus tard ne demande rien d'autre.
    /// </summary>
    public static IReadOnlyList<IModulePersistence> Persistence { get; } =
    [
        new ReferentialPersistence(),
        new CatalogPersistence(),
        new StockPersistence(),
    ];

    private static bool IsEnabled(IConfiguration configuration, string feature) =>
        configuration.GetValue("Features:" + feature, false);
}
