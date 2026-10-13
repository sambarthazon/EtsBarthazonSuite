using Barthazon.Stock.Core.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

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
        return services;
    }
}
