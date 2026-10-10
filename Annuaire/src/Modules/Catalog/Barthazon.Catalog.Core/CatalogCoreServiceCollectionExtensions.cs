using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Services;
using Barthazon.Referential.Contracts;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Catalog.Core;

public static class CatalogCoreServiceCollectionExtensions
{
    /// <summary>Services applicatifs de l'annuaire et contrats exposés aux autres modules.</summary>
    public static IServiceCollection AddCatalogCore(this IServiceCollection services)
    {
        services.AddScoped<PartSearchIndexer>();
        services.AddScoped<CatalogReferentialObserver>();
        services.AddScoped<IReferentialChangeObserver>(sp => sp.GetRequiredService<CatalogReferentialObserver>());
        services.AddScoped<IReferentialUsageProvider, CatalogUsageProvider>();
        services.AddScoped<IClassificationService, ClassificationService>();
        services.AddScoped<PartService>();
        services.AddScoped<IPartService>(sp => sp.GetRequiredService<PartService>());
        services.AddScoped<IPartLookup>(sp => sp.GetRequiredService<PartService>());
        services.AddScoped<IRevisionService, RevisionService>();
        services.AddScoped<IDocumentService, DocumentService>();
        return services;
    }
}
