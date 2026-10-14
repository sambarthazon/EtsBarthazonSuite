using Barthazon.Catalog.Contracts;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Services;
using Barthazon.Testing;

namespace Barthazon.Stock.Tests;

/// <summary>Jalon S7 : inventaire (ouverture, comptage, ligne imprévue, écarts, validation, annulation).</summary>
public sealed class InventoryTests : IAsyncLifetime
{
    private StockTestApp _app = default!;
    private StockTestData _data = default!;

    private IInventoryService Inventories => _app.Get<IInventoryService>();

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
    public async Task An_inventory_with_differences_is_validated_by_one_inventory_correction_per_cell()
    {
        var bar = await _data.CreateItemAsync("A-0001", "Rond C45 Ø50", StockUnit.Meter, StockItemKind.RawMaterial);
        var blank = await _data.CreateItemAsync("A-0002", "Débit Ø40", trackLots: true);
        var screw = await _data.CreateItemAsync("A-0003", "Vis CHC M8");
        await _data.ReceiveAsync(bar.Id, 18.5m, "MAG");
        await _data.ReceiveAsync(blank.Id, 40, "MAG", lot: "C-1");
        await _data.ReceiveAsync(blank.Id, 10, "ATELIER", lot: "C-2");
        var mag = await _data.LocationAsync("MAG");

        var opened = await Inventories.OpenAsync(new OpenInventoryInput { Title = "Inventaire magasin", LocationIds = [mag.Id] });

        opened.IsSuccess.Should().BeTrue(StockTestData.Messages(opened));
        var inventory = opened.Value;
        inventory.Number.Should().MatchRegex(@"^INV\d{4}-01$");
        inventory.Groups.Should().ContainSingle(g => g.LocationCode == "MAG");
        inventory.Lines.Should().HaveCount(2, "seules les cases du magasin sont comptées");
        var barLine = inventory.Lines.Single(l => l.SubjectCode == "A-0001");
        var blankLine = inventory.Lines.Single(l => l.SubjectCode == "A-0002");
        blankLine.Lot.Should().Be("C-1");
        blankLine.Expected.Should().Be(40);

        var counted = await Inventories.CountAsync(inventory.Id, [new InventoryCountInput(barLine.Id, 17.25m), new InventoryCountInput(blankLine.Id, 40)]);
        counted.IsSuccess.Should().BeTrue(StockTestData.Messages(counted));
        var added = await Inventories.AddLineAsync(inventory.Id, new InventoryLineInput { ItemId = screw.Id, LocationId = mag.Id, Counted = 25 });
        added.IsSuccess.Should().BeTrue(StockTestData.Messages(added));
        added.Value.DifferenceCount.Should().Be(2);
        added.Value.Lines.Single(l => l.SubjectCode == "A-0003").IsUnexpected.Should().BeTrue();

        var validated = await Inventories.ValidateAsync(inventory.Id);

        validated.IsSuccess.Should().BeTrue(StockTestData.Messages(validated));
        validated.Value.Status.Should().Be(InventoryStatus.Validated);
        (await _data.QuantityAsync(bar.Id)).Should().Be(17.25m);
        (await _data.QuantityAsync(blank.Id)).Should().Be(50, "aucun écart sur le débit, l'atelier n'est pas compté");
        (await _data.QuantityAsync(screw.Id)).Should().Be(25);
        var corrections = (await _data.Movements.SearchAsync(new MovementSearchRequest { Type = StockTransactionType.Adjustment })).Value.Items;
        corrections.Should().HaveCount(2);
        corrections.Should().OnlyContain(m => m.Reason == StockReason.Inventory && m.Reference == inventory.Number);

        var again = await Inventories.ValidateAsync(inventory.Id);
        again.IsFailure.Should().BeTrue("un inventaire validé est clos");
        (await Inventories.CountAsync(inventory.Id, [new InventoryCountInput(barLine.Id, 1)])).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task A_movement_after_the_opening_is_flagged_and_uncounted_lines_do_not_change_the_stock()
    {
        var blank = await _data.CreateItemAsync("A-0010", "Débit Ø30");
        var other = await _data.CreateItemAsync("A-0011", "Débit Ø20");
        await _data.ReceiveAsync(blank.Id, 20, "MAG");
        await _data.ReceiveAsync(other.Id, 5, "MAG");
        var inventory = (await Inventories.OpenAsync(new OpenInventoryInput())).Value;
        inventory.Scope.Should().Be("Tout le stock");

        _app.Clock.Advance(TimeSpan.FromMinutes(5));
        await _data.ReceiveAsync(blank.Id, 4, "MAG");
        var reloaded = (await Inventories.GetByNumberAsync(inventory.Number)).Value;
        reloaded.Lines.Single(l => l.SubjectCode == "A-0010").MovedSinceStart.Should().BeTrue();
        reloaded.Lines.Single(l => l.SubjectCode == "A-0011").MovedSinceStart.Should().BeFalse();

        var line = reloaded.Lines.Single(l => l.SubjectCode == "A-0010");
        (await Inventories.ValidateAsync(inventory.Id)).Errors.Should().Contain(e => e.Message.Contains("Aucune case", StringComparison.Ordinal));
        await Inventories.CountAsync(inventory.Id, [new InventoryCountInput(line.Id, 18)]);
        var validated = await Inventories.ValidateAsync(inventory.Id);

        validated.IsSuccess.Should().BeTrue(StockTestData.Messages(validated));
        (await _data.QuantityAsync(blank.Id)).Should().Be(22, "l'écart compté (−2) s'applique au stock connu, réception comprise");
        (await _data.QuantityAsync(other.Id)).Should().Be(5, "une case non comptée ne change pas");
    }

    [Fact]
    public async Task Cancelling_an_inventory_changes_nothing_and_counting_requires_an_editor()
    {
        var blank = await _data.CreateItemAsync("A-0020", "Débit Ø25");
        await _data.ReceiveAsync(blank.Id, 12, "MAG");
        var inventory = (await Inventories.OpenAsync(new OpenInventoryInput { Title = "Test" })).Value;
        var line = inventory.Lines.Single();

        _app.As(UserRole.Reader, "operateur");
        (await Inventories.CountAsync(inventory.Id, [new InventoryCountInput(line.Id, 3)])).HasError(ErrorKind.Forbidden).Should().BeTrue();
        (await Inventories.OpenAsync(new OpenInventoryInput())).IsFailure.Should().BeTrue();

        _app.As(UserRole.Editor, "chef");
        await Inventories.CountAsync(inventory.Id, [new InventoryCountInput(line.Id, 3)]);
        var cancelled = await Inventories.CancelAsync(inventory.Id);

        cancelled.Value.Status.Should().Be(InventoryStatus.Cancelled);
        (await _data.QuantityAsync(blank.Id)).Should().Be(12);
        (await Inventories.ListAsync()).Value.Single().Status.Should().Be(InventoryStatus.Cancelled);
    }

    [Fact]
    public async Task Work_in_progress_is_counted_only_when_included_and_a_counted_quantity_respects_the_unit()
    {
        var part = _app.Catalog.AddPart("P-0040", operations: [OperationType.Cutting, OperationType.Turning]);
        await _app.Get<IBatchService>().LaunchAsync(new LaunchInput { PartId = part.Id, Quantity = 12, WithoutConsumption = true });
        var bar = await _data.CreateItemAsync("A-0030", "Plat 40×10", StockUnit.Meter, StockItemKind.RawMaterial);
        await _data.ReceiveAsync(bar.Id, 6, "ATELIER");

        var withoutWip = (await Inventories.OpenAsync(new OpenInventoryInput())).Value;
        withoutWip.Lines.Should().ContainSingle(l => l.SubjectCode == "A-0030");
        await Inventories.CancelAsync(withoutWip.Id);

        var withWip = (await Inventories.OpenAsync(new OpenInventoryInput { IncludeWorkInProgress = true })).Value;
        var wip = withWip.Lines.Single(l => l.SubjectCode == "P-0040");
        wip.StageLabel.Should().Contain("op 10");
        (await Inventories.CountAsync(withWip.Id, [new InventoryCountInput(wip.Id, 10.5m)])).IsFailure.Should().BeTrue("des pièces se comptent en entiers");
        (await Inventories.CountAsync(withWip.Id, [new InventoryCountInput(wip.Id, 11)])).IsSuccess.Should().BeTrue();
        (await Inventories.ValidateAsync(withWip.Id)).IsSuccess.Should().BeTrue();

        var batch = (await _app.Get<IBatchService>().SearchAsync(new BatchSearchRequest())).Value.Single();
        batch.InProgress.Should().Be(11);
        withWip.Number.Should().EndWith("-02");
    }
}
