using Barthazon.Catalog.Contracts;
using Barthazon.Referential.Contracts;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Shared.UI.Services;
using Barthazon.Stock.Core;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Services;
using Barthazon.Stock.UI.Components;
using Barthazon.Testing;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Stock.Tests;

/// <summary>Jalon S5 : ce que le stock fournit à l'annuaire et au référentiel (section 9.2), avec la doublure de l'annuaire.</summary>
public sealed class CatalogIntegrationTests : BunitContext, IAsyncLifetime
{
    private StockTestApp _app = default!;
    private StockTestData _data = default!;

    public async Task InitializeAsync()
    {
        _app = await StockTestApp.CreateAsync();
        _data = new StockTestData(_app);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _app.AssertBalancesMatchMovementsAsync();
        await _app.DisposeAsync();
        await DisposeAsync();
    }

    [Fact]
    public async Task Stock_info_gives_finished_parts_their_location_and_threshold_and_nothing_for_untracked_parts()
    {
        var tracked = _app.Catalog.AddPart("P-0001", operations: [OperationType.Turning]);
        var untracked = _app.Catalog.AddPart("P-0002");
        var provider = _app.Get<IPartStockInfoProvider>();
        provider.Should().BeOfType<PartStockInfoProvider>();
        var details = (await _app.Get<ITrackedPartService>().TrackAsync(tracked.Id)).Value;
        await _app.Get<ITrackedPartService>().SaveSettingsAsync(tracked.Id, new TrackedPartSettingsInput { MinimumThreshold = 10, Version = details.Version });
        await _app.Get<IFinishedPartService>().ReceiveAsync(new PartReceiptInput { PartId = tracked.Id, Quantity = 4 });

        var info = await provider.GetAsync(tracked.Id);
        info.Should().Be(new PartStockInfo(4, "pce", "Magasin", 10, null));
        info!.IsBelowThreshold.Should().BeTrue();
        (await provider.GetAsync(untracked.Id)).Should().BeNull();

        var rack = await _data.CreateLocationAsync("MAG-A1");
        await _app.Get<IFinishedPartService>().ReceiveAsync(new PartReceiptInput { PartId = tracked.Id, Quantity = 1, LocationId = rack.Id });
        (await provider.GetManyAsync([tracked.Id, untracked.Id])).Should().ContainSingle().Which.Value.Location.Should().Be("Plusieurs emplacements");
    }

    [Fact]
    public async Task Stock_filters_select_tracked_parts_out_of_stock_or_below_threshold()
    {
        var empty = _app.Catalog.AddPart("P-0001");
        var low = _app.Catalog.AddPart("P-0002");
        var ok = _app.Catalog.AddPart("P-0003");
        var parts = _app.Get<ITrackedPartService>();
        foreach (var (part, threshold, quantity) in new[] { (empty, 5, 0), (low, 5, 3), (ok, 5, 8) })
        {
            var details = (await parts.TrackAsync(part.Id)).Value;
            await parts.SaveSettingsAsync(part.Id, new TrackedPartSettingsInput { MinimumThreshold = threshold, Version = details.Version });
            if (quantity > 0)
            {
                await _app.Get<IFinishedPartService>().ReceiveAsync(new PartReceiptInput { PartId = part.Id, Quantity = quantity });
            }
        }

        var filter = _app.Get<IPartStockFilterProvider>();
        filter.IsAvailable.Should().BeTrue();
        (await filter.GetPartIdsAsync(PartStockFilter.OutOfStock)).Should().BeEquivalentTo([empty.Id]);
        (await filter.GetPartIdsAsync(PartStockFilter.BelowThreshold)).Should().BeEquivalentTo([empty.Id, low.Id]);
    }

    [Fact]
    public async Task Materials_customers_and_machines_used_by_the_stock_are_reported_as_used()
    {
        var material = (await _app.Get<IMaterialLookup>().QuickCreateAsync("Acier C45", "C45", MaterialFamily.Steel)).Value.Id;
        var customer = (await _app.Get<ICustomerLookup>().QuickCreateAsync("DUPONT", "Dupont")).Value.Id;
        await _app.Get<IItemService>().SaveAsync(null, new ItemInput { Code = "A-0001", Designation = "Brut", MaterialId = material, OwnerCustomerId = customer });
        var machine = Guid.NewGuid();
        var part = _app.Catalog.AddPart("P-0001");
        _app.Catalog.AddOperation(part.RevisionId, 10, OperationType.Turning, machine);
        await _app.Get<IBatchService>().LaunchAsync(new LaunchInput { PartId = part.Id, Quantity = 1, WithoutConsumption = true });

        var usage = _app.Services.GetServices<IReferentialUsageProvider>().OfType<StockReferentialUsageProvider>().Single();
        (await usage.CountUsagesAsync(ReferentialItemKind.Material)).Should().Contain(material, 1);
        (await usage.CountUsagesAsync(ReferentialItemKind.Customer)).Should().Contain(customer, 1);
        (await usage.CountUsagesAsync(ReferentialItemKind.Machine)).Should().Contain(machine, 1);
    }

    [Fact]
    public async Task The_stock_panel_of_the_catalog_part_page_offers_to_track_an_untracked_part_and_warns_about_superseded_stock()
    {
        var part = _app.Catalog.AddPart("P-0001", "Bride", "B", OperationType.Turning);
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSharedUi();
        Services.AddSingleton<ICurrentUser>(_app.User);
        Services.AddSingleton(_app.Get<ITrackedPartService>());
        Services.AddSingleton(_app.Get<IBatchService>());
        Services.AddSingleton(_app.Get<IFinishedPartService>());
        Services.AddSingleton(_app.Get<StockOptions>());

        var cut = Render<PartStockPanel>(p => p.Add(x => x.PartId, part.Id));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Pièce non suivie en stock").And.Contain("Suivre cette pièce en stock"));

        await _app.Get<IFinishedPartService>().ReceiveAsync(new PartReceiptInput { PartId = part.Id, Quantity = 5, Lot = "L2026-001" });
        _app.Catalog.PutInForce(_app.Catalog.AddRevision(part.Id, "C").Id);
        cut = Render<PartStockPanel>(p => p.Add(x => x.PartId, part.Id));
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("L2026-001").And.Contain("remplacé").And.Contain("plus en vigueur"));
    }
}
