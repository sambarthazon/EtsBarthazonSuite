using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Services;
using Barthazon.Portal.Hosting;
using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Kernel.Modules;
using Barthazon.Stock.Core;
using Barthazon.Stock.UI;
using Barthazon.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Integration.Tests;

/// <summary>Activation du stock par <c>Features:Stock</c> (section 9.3 de la spécification du stock).</summary>
public sealed class StockActivationTests
{
    private static IConfiguration Features(bool stock) =>
        new ConfigurationBuilder().AddInMemoryCollection([new("Features:Stock", stock ? "true" : "false")]).Build();

    [Fact]
    public void Disabled_stock_is_a_greyed_tile_and_enabled_stock_is_the_real_module()
    {
        var disabled = PortalModules.Create(Features(stock: false)).Single(m => m.Key == "stock");
        disabled.State.Should().Be(ModuleState.ComingSoon);
        disabled.UiAssembly.Should().BeNull();

        var enabled = PortalModules.Create(Features(stock: true)).Single(m => m.Key == "stock");
        enabled.Should().BeOfType<StockModule>();
        enabled.State.Should().Be(ModuleState.Available);
        enabled.MenuItems.Select(i => i.Href).Should().Contain(["stock", "stock/saisie", "stock/lancements", "stock/pieces", "stock/articles", "stock/mouvements", "stock/inventaires"]);
    }

    [Fact]
    public void Stock_persistence_is_always_declared_by_the_portal()
    {
        PortalModules.Persistence.Select(p => p.Key).Should().Equal("referential", "catalog", "stock");
    }

    [Fact]
    public async Task With_stock_disabled_the_catalog_works_and_stock_tables_are_still_created()
    {
        await using var app = await IntegrationTestApp.CreateAsync(stockEnabled: false);

        app.Services.GetService<StockOptions>().Should().BeNull("les services du stock ne sont pas enregistrés");
        app.Get<IPartStockInfoProvider>().Should().BeOfType<UnavailablePartInfoProvider>();
        app.Get<IPartStockFilterProvider>().IsAvailable.Should().BeFalse();
        (await app.Get<IPartService>().SearchAsync(new PartSearchRequest())).IsSuccess.Should().BeTrue();

        var stock = app.Services.GetServices<ModuleDatabase>().Single(d => d.Info.Key == "stock");
        await using var context = stock.CreateContext();
        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty("les migrations du stock sont appliquées même désactivé");
        (await context.Database.GetAppliedMigrationsAsync()).Should().NotBeEmpty();
    }
}
