using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Services;
using Barthazon.Portal.Hosting;
using Barthazon.Referential.Core.Services;
using Barthazon.Shared.UI.Services;
using Barthazon.Stock.Core.Services;
using Barthazon.Stock.UI;
using Barthazon.Stock.UI.Components;
using Barthazon.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Integration.Tests;

/// <summary>Stock et annuaire réels branchés ensemble (section 6.5, tests d'intégration).</summary>
public sealed class StockWithCatalogTests : IAsyncLifetime
{
    private TestApplication _app = default!;

    // Hôte complet : services de l'interface du stock compris (recherche globale, emplacements d'interface).
    public async Task InitializeAsync() => _app = await IntegrationTestApp.CreateAsync(services =>
    {
        services.AddSharedUi();
        new StockModule().ConfigureServices(services, new ConfigurationBuilder().Build());
    });

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task A_real_catalog_part_is_launched_with_its_routing_which_stays_frozen()
    {
        var customer = (await _app.Get<ICustomerService>().CreateAsync(new CustomerInput { Code = "DUPONT", Name = "Dupont Énergie" })).Value.Id;
        var part = (await _app.Get<IPartService>().CreateAsync(new PartCreateInput
        {
            InternalReference = "P-0012", Designation = "Bride Ø120 gauche", CustomerId = customer, FirstIndex = "B", PutInForce = true,
        })).Value;
        var revisionId = part.InForceRevisionId!.Value;
        var revision = (await _app.Get<IPartService>().GetRevisionAsync(part.Id, revisionId)).Value;
        var input = RevisionInput.From(revision);
        input.Operations =
        [
            new OperationInput { Number = 10, Type = OperationType.Cutting, Description = "Débit" },
            new OperationInput { Number = 20, Type = OperationType.Turning },
            new OperationInput { Number = 30, Type = OperationType.Milling },
            new OperationInput { Number = 40, Type = OperationType.Inspection },
        ];
        (await _app.Get<IRevisionService>().UpdateAsync(part.Id, revisionId, input)).IsSuccess.Should().BeTrue();

        var batch = await _app.Get<IBatchService>().LaunchAsync(new LaunchInput { PartId = part.Id, Quantity = 30, WithoutConsumption = true });
        batch.IsSuccess.Should().BeTrue(string.Join(" ", batch.Errors.Select(e => e.Message)));
        batch.Value.Steps.Select(s => s.OperationNumber).Should().Equal(10, 20, 30, 40);
        batch.Value.PartReference.Should().Be("P-0012");

        // L'annuaire ajoute une opération 35 : le lancement garde sa gamme figée ; un nouveau lancement prend la nouvelle.
        revision = (await _app.Get<IPartService>().GetRevisionAsync(part.Id, revisionId)).Value;
        input = RevisionInput.From(revision);
        input.Operations.Add(new OperationInput { Number = 35, Type = OperationType.Deburring });
        (await _app.Get<IRevisionService>().UpdateAsync(part.Id, revisionId, input)).IsSuccess.Should().BeTrue();

        (await _app.Get<IBatchService>().GetAsync(batch.Value.Id)).Value.Steps.Select(s => s.OperationNumber).Should().Equal(10, 20, 30, 40);
        var next = await _app.Get<IBatchService>().LaunchAsync(new LaunchInput { PartId = part.Id, Quantity = 5, WithoutConsumption = true });
        next.Value.Steps.Select(s => s.OperationNumber).Should().Equal(10, 20, 30, 35, 40);
        (await _app.Get<ITrackedPartService>().GetAsync(part.Id)).Value.IsTracked.Should().BeTrue("une pièce est suivie à son premier mouvement");
    }

    [Fact]
    public async Task The_catalog_list_filter_out_of_stock_uses_the_stock_and_global_search_finds_stock_objects()
    {
        var customer = (await _app.Get<ICustomerService>().CreateAsync(new CustomerInput { Code = "DUPONT", Name = "Dupont Énergie" })).Value.Id;
        var parts = _app.Get<IPartService>();
        var empty = (await parts.CreateAsync(new PartCreateInput { InternalReference = "P-0001", Designation = "Axe", CustomerId = customer, FirstIndex = "A", PutInForce = true })).Value;
        var stocked = (await parts.CreateAsync(new PartCreateInput { InternalReference = "P-0002", Designation = "Bague", CustomerId = customer, FirstIndex = "A", PutInForce = true })).Value;
        await parts.CreateAsync(new PartCreateInput { InternalReference = "P-0003", Designation = "Non suivie", CustomerId = customer, FirstIndex = "A", PutInForce = true });
        await _app.Get<ITrackedPartService>().TrackAsync(empty.Id);
        await _app.Get<IFinishedPartService>().ReceiveAsync(new PartReceiptInput { PartId = stocked.Id, Quantity = 3 });

        var outOfStock = await parts.SearchAsync(new PartSearchRequest { StockFilter = PartStockFilter.OutOfStock });
        outOfStock.Value.Items.Select(p => p.InternalReference).Should().Equal("P-0001");
        (await _app.Get<IPartStockInfoProvider>().GetAsync(stocked.Id))!.Quantity.Should().Be(3);

        await _app.Get<IItemService>().SaveAsync(null, new ItemInput { Code = "A-0003", Designation = "Brut forgé bride Ø125" });
        var search = _app.Services.GetServices<IGlobalSearchProvider>().Single(p => p.GetType().Name == "StockGlobalSearchProvider");
        (await search.SearchAsync("bride", 10, CancellationToken.None)).Select(h => h.Href).Should().Contain("stock/articles/A-0003");
        (await search.SearchAsync("MAG", 10, CancellationToken.None)).Select(h => h.Href).Should().Contain("stock/emplacements/MAG");
    }

    [Fact]
    public void The_stock_fills_the_catalog_slots_only_when_enabled()
    {
        static UiExtensionRegistry Registry(bool stock)
        {
            var configuration = new ConfigurationBuilder().AddInMemoryCollection([new("Features:Stock", stock ? "true" : "false")]).Build();
            var services = new ServiceCollection();
            services.AddSharedUi();
            foreach (var module in PortalModules.Create(configuration))
            {
                module.ConfigureServices(services, configuration);
            }

            return new UiExtensionRegistry(services.Where(d => d.ServiceType == typeof(UiExtension)).Select(d => (UiExtension)d.ImplementationInstance!));
        }

        var enabled = Registry(stock: true);
        enabled.For(CatalogUiSlots.PartStockTab).Select(e => e.ComponentType).Should().Equal(typeof(PartStockPanel));
        enabled.For(CatalogUiSlots.DashboardCards).Select(e => e.ComponentType).Should().Equal(typeof(StockAlertsCard), typeof(StockWorkInProgressCard));
        enabled.For(UiSlots.CustomerDetails).Select(e => e.ComponentType).Should().Contain(typeof(CustomerItemsSection));

        var disabled = Registry(stock: false);
        disabled.For(CatalogUiSlots.PartStockTab).Should().BeEmpty();
        disabled.For(CatalogUiSlots.DashboardCards).Should().BeEmpty();
    }
}
