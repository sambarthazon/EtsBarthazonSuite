using Barthazon.Catalog.Core;
using Barthazon.Catalog.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Testing;

/// <summary>Hôte de test de l'annuaire : socle + module Catalog.</summary>
public static class CatalogTestApp
{
    public static Task<TestApplication> CreateAsync(Action<IServiceCollection>? configure = null, Dictionary<string, string?>? settings = null) =>
        TestApplication.CreateAsync(configure, settings, AddCatalog);

    public static void AddCatalog(TestApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddModule(new CatalogPersistence(), s => s.AddCatalogCore())
            .WithSetting("Catalog:ReferencePattern", "P-{0:0000}");
    }
}
