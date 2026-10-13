using Barthazon.Catalog.Contracts;
using Barthazon.Stock.Core;
using Barthazon.Stock.Infrastructure;
using Barthazon.Stock.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Testing;

/// <summary>Hôte de test du stock : socle + module Stock + doublure de l'annuaire (<see cref="FakeCatalog"/>).</summary>
public static class StockTestApp
{
    public static async Task<(TestApplication App, FakeCatalog Catalog)> CreateAsync(
        Action<IServiceCollection>? configure = null, Dictionary<string, string?>? settings = null)
    {
        var catalog = new FakeCatalog();
        var app = await TestApplication.CreateAsync(
            services =>
            {
                services.AddSingleton(catalog);
                services.AddSingleton<IPartLookup>(catalog);
                services.AddSingleton<IPartRoutingLookup>(catalog);
                configure?.Invoke(services);
            },
            settings,
            AddStock);
        return (app, catalog);
    }

    public static void AddStock(TestApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddModule(new StockPersistence(), s => s.AddStockCore())
            .WithSetting("Features:Stock", "true");
    }
}
