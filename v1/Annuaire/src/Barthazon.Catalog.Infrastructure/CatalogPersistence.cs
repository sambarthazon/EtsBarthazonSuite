using Barthazon.Catalog.Core.Persistence;
using Barthazon.Catalog.Core.Services;
using Barthazon.Catalog.Infrastructure.Exchange;
using Barthazon.Catalog.Infrastructure.Persistence;
using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Kernel.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Catalog.Infrastructure;

/// <summary>Persistance de l'annuaire : son contexte et ses migrations (tables cat_*), repositories.</summary>
public sealed class CatalogPersistence : IModulePersistence
{
    public string Key => "catalog";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDatabase<CatalogDbContext>(CatalogDbContext.Info, (sp, options) => new CatalogDbContext(options, sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<IUnitOfWorkFactory<ICatalogUnitOfWork>, CatalogUnitOfWorkFactory>();
        services.AddSingleton<IPartSpreadsheet, ClosedXmlPartSpreadsheet>();
    }
}
