using Barthazon.Stock.Contracts;
using Barthazon.Stock.Core.Services;
using Microsoft.Extensions.Configuration;
using Barthazon.Catalog.Contracts;
using Barthazon.Referential.Contracts;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barthazon.Stock.Core;

public static class StockCoreServiceCollectionExtensions
{
    /// <summary>Services applicatifs du stock (enregistrés seulement si la fonction <c>Stock</c> est active).</summary>
    public static IServiceCollection AddStockCore(this IServiceCollection services)
    {
        services.AddSingleton(sp => StockOptions.From(sp.GetRequiredService<IConfiguration>()));
        services.AddScoped<StockAccess>();
        services.AddScoped<StockLedger>();
        services.AddScoped<ILocationService, LocationService>();
        services.AddScoped<IItemService, ItemService>();
        services.AddScoped<MovementService>();
        services.AddScoped<IMovementService>(sp => sp.GetRequiredService<MovementService>());
        services.AddScoped<IBalanceVerificationService, BalanceVerificationService>();
        services.AddScoped<ITrackedPartService, TrackedPartService>();
        services.AddScoped<BatchService>();
        services.AddScoped<IBatchService>(sp => sp.GetRequiredService<BatchService>());
        services.AddScoped<IFinishedPartService, FinishedPartService>();
        services.AddScoped<IStockDashboardService, StockDashboardService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IStockImportService, StockImportService>();
        services.AddScoped<IStockExportService, StockExportService>();
        services.AddScoped<Demo.StockDemoData>();

        // Contrats exposés aux futures applications (commandes, pointage).
        services.AddScoped<StockContractsService>();
        services.AddScoped<IStockQuery>(sp => sp.GetRequiredService<StockContractsService>());
        services.AddScoped<IBatchLookup>(sp => sp.GetRequiredService<StockContractsService>());
        services.AddScoped<IStockShipments>(sp => sp.GetRequiredService<StockContractsService>());

        // Points d'extension de l'annuaire et du référentiel (section 9.2) : remplacent les implémentations « non disponible ».
        services.Replace(ServiceDescriptor.Scoped<IPartStockInfoProvider, PartStockInfoProvider>());
        services.Replace(ServiceDescriptor.Scoped<IPartStockFilterProvider, PartStockFilterProvider>());
        services.AddScoped<IReferentialUsageProvider, StockReferentialUsageProvider>();
        return services;
    }
}
