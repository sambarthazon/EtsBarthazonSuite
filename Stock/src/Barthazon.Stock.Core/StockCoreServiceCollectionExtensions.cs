using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Stock.Core;

public static class StockCoreServiceCollectionExtensions
{
    /// <summary>Services applicatifs du stock (enregistrés seulement si la fonction <c>Stock</c> est active).</summary>
    public static IServiceCollection AddStockCore(this IServiceCollection services)
    {
        services.AddSingleton(sp => StockOptions.From(sp.GetRequiredService<IConfiguration>()));
        return services;
    }
}
