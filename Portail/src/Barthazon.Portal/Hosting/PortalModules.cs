using Barthazon.Catalog.Infrastructure;
using Barthazon.Catalog.UI;
using Barthazon.Portal.Resources;
using Barthazon.Referential.Infrastructure;
using Barthazon.Referential.UI;
using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Kernel.Modules;
using MudBlazor;

namespace Barthazon.Portal.Hosting;

/// <summary>
/// Liste des modules hébergés par le portail. Pour ajouter une application : créer son module
/// (Contracts / Core / Infrastructure / UI) et l'ajouter ici.
/// </summary>
internal static class PortalModules
{
    public static IReadOnlyList<IModule> All { get; } =
    [
        new ReferentialModule(),
        new CatalogModule(),

        // Applications à venir : tuiles grisées, aucun code métier.
        new ComingSoonModule("stock", PortalTexts.StockName, PortalTexts.StockDescription, Icons.Material.Filled.Inventory, 20),
        new ComingSoonModule("orders", PortalTexts.OrdersName, PortalTexts.OrdersDescription, Icons.Material.Filled.LocalShipping, 30),
        new ComingSoonModule("time-tracking", PortalTexts.TimeTrackingName, PortalTexts.TimeTrackingDescription, Icons.Material.Filled.Schedule, 40),
    ];

    /// <summary>Persistance de chaque module (mappings EF Core, repositories).</summary>
    public static IReadOnlyList<IModulePersistence> Persistence { get; } =
    [
        new ReferentialPersistence(),
        new CatalogPersistence(),
    ];
}
