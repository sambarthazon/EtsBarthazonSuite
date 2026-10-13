using Barthazon.Catalog.Core;
using Barthazon.Catalog.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Testing;

/// <summary>Hôte de test du portail complet : socle + annuaire (+ stock).</summary>
public static class IntegrationTestApp
{
    public static Task<TestApplication> CreateAsync(Action<IServiceCollection>? configure = null, Dictionary<string, string?>? settings = null) =>
        TestApplication.CreateAsync(configure, settings, AddApplications);

    public static void AddApplications(TestApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddModule(new CatalogPersistence(), s => s.AddCatalogCore())
            .WithSetting("Catalog:ReferencePattern", "P-{0:0000}");
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
