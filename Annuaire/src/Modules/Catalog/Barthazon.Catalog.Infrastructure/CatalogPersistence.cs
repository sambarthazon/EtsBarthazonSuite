using System.Reflection;
using Barthazon.Catalog.Core.Persistence;
using Barthazon.Catalog.Infrastructure.Persistence;
using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Kernel.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Catalog.Infrastructure;

/// <summary>Persistance de l'annuaire : mappings EF Core (tables cat_*) et repositories.</summary>
public sealed class CatalogPersistence : IModulePersistence
{
    public string Key => "catalog";

    public Assembly MappingsAssembly => typeof(CatalogPersistence).Assembly;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IUnitOfWorkFactory<ICatalogUnitOfWork>, CatalogUnitOfWorkFactory>();
    }
}
