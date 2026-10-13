using Barthazon.Catalog.Core;
using Barthazon.Catalog.Infrastructure;
using Barthazon.Stock.Core;
using Barthazon.Stock.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Testing;

/// <summary>
/// Hôte de test du portail complet : socle + annuaire + stock réels. Comme dans le portail, la persistance du stock est
/// toujours présente (tables créées) ; ses services ne sont enregistrés que si le stock est activé.
/// </summary>
public static class IntegrationTestApp
{
    public static Task<TestApplication> CreateAsync(Action<IServiceCollection>? configure = null, Dictionary<string, string?>? settings = null, bool stockEnabled = true) =>
        TestApplication.CreateAsync(configure, settings, builder => AddApplications(builder, stockEnabled));

    public static void AddApplications(TestApplicationBuilder builder) => AddApplications(builder, stockEnabled: true);

    public static void AddApplications(TestApplicationBuilder builder, bool stockEnabled)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddModule(new CatalogPersistence(), s => s.AddCatalogCore())
            .WithSetting("Catalog:ReferencePattern", "P-{0:0000}")
            .AddModule(new StockPersistence(), s =>
            {
                if (stockEnabled)
                {
                    s.AddStockCore();
                }
            })
            .WithSetting("Features:Stock", stockEnabled ? "true" : "false");
    }

    /// <summary>Copie une base de test versionnée (dossier Fixtures) dans un dossier temporaire.</summary>
    public static string CopyFixture(string fileName)
    {
        var directory = Path.Combine(Path.GetTempPath(), "bz-fixture-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, fileName);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", fileName), target);
        return target;
    }
}
