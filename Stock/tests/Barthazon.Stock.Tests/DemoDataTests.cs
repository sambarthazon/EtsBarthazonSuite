using Barthazon.Catalog.Contracts;
using Barthazon.Stock.Contracts;
using Barthazon.Stock.Core.Demo;
using Barthazon.Stock.Core.Services;
using Barthazon.Testing;

namespace Barthazon.Stock.Tests;

/// <summary>Jalon S8 : données de démonstration du stock (passent par les services, pièces retrouvées par leur référence).</summary>
public sealed class DemoDataTests : IAsyncLifetime
{
    private StockTestApp _app = default!;

    public async Task InitializeAsync() => _app = await StockTestApp.CreateAsync();

    public async Task DisposeAsync()
    {
        await _app.AssertBalancesMatchMovementsAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Without_the_catalog_demo_parts_only_locations_and_items_are_loaded_and_a_second_run_does_nothing()
    {
        await _app.Get<StockDemoData>().SeedAsync();
        await _app.Get<StockDemoData>().SeedAsync();

        var items = (await _app.Get<IItemService>().SearchAsync(new ItemSearchRequest())).Value;
        items.Should().HaveCount(11);
        items.Single(i => i.Code == "A-0002").Quantity.Should().Be(12);
        (await _app.Get<IBatchService>().SearchAsync(new BatchSearchRequest { Status = null })).Value.Should().BeEmpty();
        (await _app.Get<ILocationService>().ListAsync(false)).Value.Select(l => l.Code).Should().Contain(["RAYON-A", "RAYON-B", "TTS"]);
    }

    [Fact]
    public async Task With_the_catalog_demo_parts_batches_at_several_stages_a_shipment_and_superseded_finished_parts_are_loaded()
    {
        OperationType[] turning = [OperationType.Cutting, OperationType.Turning, OperationType.Turning, OperationType.Inspection];
        var shaft = _app.Catalog.AddPart("P-0001", "Arbre de transmission Ø40", "B", [.. turning, OperationType.Grinding]);
        var indexC = _app.Catalog.AddRevision(shaft.Id, "C");
        foreach (var (number, type) in new[] { (10, OperationType.Cutting), (20, OperationType.Turning), (30, OperationType.Turning), (40, OperationType.Inspection), (50, OperationType.Grinding) })
        {
            _app.Catalog.AddOperation(indexC.Id, number, type);
        }

        _app.Catalog.PutInForce(indexC.Id);
        _app.Catalog.AddPart("P-0002", "Bague de friction", "A", turning);
        _app.Catalog.AddPart("P-0004", "Axe de chape Ø20", "A", turning);
        _app.Catalog.AddPart("P-0005", "Axe de vérin", "A", turning);
        var spacer = _app.Catalog.AddPart("P-0006", "Entretoise 10 mm", "A", turning);
        _app.Catalog.AddPart("P-0007", "Entretoise 25 mm", "A", turning);
        _app.Catalog.AddPart("P-0008", "Support moteur", "A", OperationType.Cutting, OperationType.Milling, OperationType.Deburring);
        _app.Catalog.AddPart("P-0009", "Arbre intermédiaire", "A", turning);

        await _app.Get<StockDemoData>().SeedAsync();

        var batches = (await _app.Get<IBatchService>().SearchAsync(new BatchSearchRequest { Status = null })).Value;
        batches.Should().HaveCount(5);
        batches.Should().ContainSingle(b => b.Status == BatchStatus.Completed && b.PartReference == "P-0006");
        batches.Single(b => b.PartReference == "P-0001").InProgress.Should().Be(13);
        var shaftStock = (await _app.Get<IFinishedPartService>().GetFinishedStockAsync(shaft.Id)).Value;
        shaftStock.Cells.Should().Contain(c => c.RevisionIndex == "B" && !c.RevisionIsInForce && c.Quantity == 6);
        (await _app.Get<IStockQuery>().GetFinishedStockAsync(spacer.Id)).Sum(l => l.Quantity).Should().Be(28, "48 finies, 20 livrées");
        var common = (await _app.Get<ITrackedPartService>().ListPartsMadeFromItemAsync((await _app.Get<IItemService>().GetByCodeAsync("A-0003")).Value.Id)).Value;
        common.Select(u => u.Reference).Should().BeEquivalentTo(["P-0006", "P-0007"], "un brut commun à deux pièces");
        (await _app.Get<ITrackedPartService>().ListPartsMadeFromPartAsync(shaft.Id)).Value.Should().ContainSingle(u => u.Reference == "P-0009" && u.SourceAfterOperation == 20);
        var dashboard = (await _app.Get<IStockDashboardService>().GetAsync()).Value;
        dashboard.Should().NotBeNull();
    }
}
