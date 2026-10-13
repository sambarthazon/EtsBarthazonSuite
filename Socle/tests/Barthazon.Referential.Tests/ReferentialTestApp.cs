using Barthazon.Catalog.Core;
using Barthazon.Catalog.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Testing;

/// <summary>
/// Hôte de test du référentiel. TEMPORAIRE (jalon R1) : tant que les migrations sont communes à tout le schéma,
/// le modèle doit contenir les tables de l'annuaire. Au jalon R2, le socle a son propre contexte et ce détour disparaît.
/// </summary>
public static class ReferentialTestApp
{
    public static Task<TestApplication> CreateAsync(Action<IServiceCollection>? configure = null, Dictionary<string, string?>? settings = null) =>
        TestApplication.CreateAsync(configure, settings, b => b.AddModule(new CatalogPersistence(), s => s.AddCatalogCore()));
}
