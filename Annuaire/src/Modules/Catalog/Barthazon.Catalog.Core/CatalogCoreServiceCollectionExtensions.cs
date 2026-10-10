using Barthazon.Catalog.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Catalog.Core;

public static class CatalogCoreServiceCollectionExtensions
{
    /// <summary>Services applicatifs de l'annuaire et contrats exposés aux autres modules.</summary>
    public static IServiceCollection AddCatalogCore(this IServiceCollection services)
    {
        services.AddScoped<IClassificationService, ClassificationService>();
        return services;
    }
}
