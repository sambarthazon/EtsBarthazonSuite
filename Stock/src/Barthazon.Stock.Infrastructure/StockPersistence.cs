using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Stock.Core.Persistence;
using Barthazon.Stock.Core.Services;
using Barthazon.Stock.Infrastructure.Exchange;
using Barthazon.Stock.Infrastructure.Persistence;
using Barthazon.Stock.Infrastructure.Seeding;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Stock.Infrastructure;

/// <summary>
/// Persistance du stock : son contexte et ses migrations (tables stk_*), repositories. Toujours enregistrée, même si la
/// fonction Stock est désactivée : les tables existent et sont sauvegardées, activer le stock ne demande rien d'autre.
/// </summary>
public sealed class StockPersistence : IModulePersistence
{
    public string Key => "stock";

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddModuleDatabase<StockDbContext>(StockDbContext.Info, (sp, options) => new StockDbContext(options, sp.GetRequiredService<TimeProvider>()));
        services.AddScoped<IUnitOfWorkFactory<IStockUnitOfWork>, StockUnitOfWorkFactory>();
        services.AddSingleton<IDataSeeder, StockSeeder>();
        services.AddSingleton<IStockSpreadsheet, ClosedXmlStockSpreadsheet>();
    }
}
