using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Services;
using Barthazon.Testing;

namespace Barthazon.Stock.Tests;

/// <summary>Jalon S1 : emplacements et articles.</summary>
public sealed class LocationAndItemTests : IAsyncLifetime
{
    private StockTestApp _app = default!;
    private StockTestData _data = default!;

    public async Task InitializeAsync()
    {
        _app = await StockTestApp.CreateAsync();
        _data = new StockTestData(_app);
    }

    public async Task DisposeAsync()
    {
        await _app.AssertBalancesMatchMovementsAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Location_codes_are_unique_and_a_location_with_stock_cannot_be_archived()
    {
        var rack = await _data.CreateLocationAsync("mag-a1");
        rack.Code.Should().Be("MAG-A1");
        (await _data.Locations.SaveAsync(null, new LocationInput { Code = "MAG-A1", Name = "Doublon" })).IsFailure.Should().BeTrue();
        (await _data.Locations.SaveAsync(null, new LocationInput { Code = "MAG A1", Name = "Espace" })).IsFailure.Should().BeTrue();

        var item = await _data.CreateItemAsync("A-0001");
        await _data.ReceiveAsync(item.Id, 3, "MAG-A1");
        (await _data.Locations.SetArchivedAsync(rack.Id, true)).FirstError!.Message.Should().Contain("contient du stock");

        var contents = (await _data.Locations.GetByCodeAsync("mag-a1")).Value;
        contents.Location.HasStock.Should().BeTrue();
        contents.Contents.Should().ContainSingle().Which.Should().Match<StockCellView>(c => c.SubjectCode == "A-0001" && c.Quantity == 3);

        var cell = contents.Contents.Single().Key;
        await _data.Movements.TransferAsync(new TransferInput { From = cell, ToLocationId = (await _data.LocationAsync("MAG")).Id, Quantity = 3 });
        (await _data.Locations.SetArchivedAsync(rack.Id, true)).IsSuccess.Should().BeTrue();
        (await _data.Locations.ListAsync(includeArchived: false)).Value.Select(l => l.Code).Should().NotContain("MAG-A1");
    }

    [Fact]
    public async Task Item_codes_are_proposed_from_the_pattern_and_unique()
    {
        (await _data.Items.SuggestCodeAsync()).Value.Should().Be("A-0001");
        await _data.CreateItemAsync("A-0001");
        await _data.CreateItemAsync("A-0003");
        (await _data.Items.SuggestCodeAsync()).Value.Should().Be("A-0002");
        (await _data.Items.SaveAsync(null, new ItemInput { Code = "a-0001", Designation = "Doublon" })).FirstError!.Field.Should().Be(nameof(ItemInput.Code));
    }

    [Fact]
    public async Task An_item_with_stock_keeps_its_unit_and_cannot_be_archived()
    {
        var item = await _data.CreateItemAsync("A-0001", unit: StockUnit.Meter);
        await _data.ReceiveAsync(item.Id, 2.5m);
        var details = (await _data.Items.GetAsync(item.Id)).Value;

        var input = new ItemInput { Code = details.Code, Designation = details.Designation, Unit = StockUnit.Kilogram, Version = details.Version };
        (await _data.Items.SaveAsync(item.Id, input)).FirstError!.Field.Should().Be(nameof(ItemInput.Unit));
        (await _data.Items.SetArchivedAsync(item.Id, true)).FirstError!.Message.Should().Contain("stock");
    }

    [Fact]
    public async Task Concurrent_edits_of_an_item_are_detected()
    {
        var item = await _data.CreateItemAsync("A-0001", "Barre");
        var stale = (await _data.Items.GetAsync(item.Id)).Value.Version;
        (await _data.Items.SaveAsync(item.Id, new ItemInput { Code = "A-0001", Designation = "Barre C45", Version = stale })).IsSuccess.Should().BeTrue();
        (await _data.Items.SaveAsync(item.Id, new ItemInput { Code = "A-0001", Designation = "Barre 42CD4", Version = stale })).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Item_search_is_tolerant_and_filters_by_kind_owner_and_threshold()
    {
        await _data.CreateItemAsync("A-0001", "Barre C45 Ø60", StockUnit.Meter, StockItemKind.RawMaterial, threshold: 6);
        var forged = await _data.CreateItemAsync("A-0002", "Brut forgé bride Ø125", kind: StockItemKind.Blank, threshold: 10);
        await _data.ReceiveAsync(forged.Id, 60);

        (await _data.Items.SearchAsync(new ItemSearchRequest { Query = "forge bride" })).Value.Select(i => i.Code).Should().Equal("A-0002");
        (await _data.Items.SearchAsync(new ItemSearchRequest { Kind = StockItemKind.RawMaterial })).Value.Select(i => i.Code).Should().Equal("A-0001");
        var below = (await _data.Items.SearchAsync(new ItemSearchRequest { BelowThresholdOnly = true })).Value;
        below.Select(i => i.Code).Should().Equal("A-0001");
        below.Single().IsBelowThreshold.Should().BeTrue();
        (await _data.Items.SearchAsync(new ItemSearchRequest { Owner = ItemOwnerFilter.Customers })).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Indicative_value_is_quantity_times_reference_cost()
    {
        var item = await _data.CreateItemAsync("A-0001", unit: StockUnit.Meter);
        var details = (await _data.Items.GetAsync(item.Id)).Value;
        await _data.Items.SaveAsync(item.Id, new ItemInput { Code = "A-0001", Designation = "Barre", Unit = StockUnit.Meter, ReferenceUnitCost = 12.5m, Version = details.Version });
        await _data.ReceiveAsync(item.Id, 18);

        (await _data.Items.GetAsync(item.Id)).Value.Value.Should().Be(225m);
    }
}
