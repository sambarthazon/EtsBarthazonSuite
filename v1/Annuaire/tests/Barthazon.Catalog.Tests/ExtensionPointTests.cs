using Barthazon.Catalog.Contracts;
using Barthazon.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Catalog.Tests;

/// <summary>Points d'extension pour les futures applications (section 6.6).</summary>
public sealed class ExtensionPointTests
{
    private sealed class FakeStock : IPartStockInfoProvider
    {
        public bool IsAvailable => true;

        public Task<PartStockInfo?> GetAsync(Guid partId, CancellationToken cancellationToken = default) =>
            Task.FromResult<PartStockInfo?>(new PartStockInfo(3, "pcs", "B3", 10, null));

        public Task<IReadOnlyDictionary<Guid, PartStockInfo>> GetManyAsync(IEnumerable<Guid> partIds, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<Guid, PartStockInfo>>(partIds.ToDictionary(id => id, _ => new PartStockInfo(3, "pcs", "B3", 10, null)));
    }

    [Fact]
    public async Task By_default_stock_deliveries_and_orders_are_not_available()
    {
        await using var app = await CatalogTestApp.CreateAsync();

        app.Get<IPartStockInfoProvider>().IsAvailable.Should().BeFalse();
        (await app.Get<IPartStockInfoProvider>().GetAsync(Guid.NewGuid())).Should().BeNull();
        (await app.Get<IPartDeliveryInfoProvider>().GetHistoryAsync(Guid.NewGuid())).Should().BeEmpty();
        (await app.Get<IPartOrderInfoProvider>().GetOpenOrdersAsync(Guid.NewGuid())).Should().BeEmpty();
    }

    [Fact]
    public async Task A_future_module_replaces_the_provider_without_changing_the_catalog()
    {
        await using var app = await CatalogTestApp.CreateAsync(s => s.AddSingleton<IPartStockInfoProvider, FakeStock>());

        var stock = await app.Get<IPartStockInfoProvider>().GetAsync(Guid.NewGuid());

        stock!.IsBelowThreshold.Should().BeTrue();
    }

    [Fact]
    public async Task Other_modules_read_parts_through_the_lookup_contract()
    {
        await using var app = await CatalogTestApp.CreateAsync();
        var data = new CatalogTestData(app);
        await data.SeedAsync();
        await data.CreatePartAsync("P-0300", "Axe", customerReference: "X-1");

        var lookup = app.Get<IPartLookup>();

        (await lookup.GetByReferenceAsync("P-0300"))!.CurrentRevisionStatus.Should().Be(RevisionStatus.InForce);
        (await lookup.SearchAsync("x1")).Should().ContainSingle();
    }
}
