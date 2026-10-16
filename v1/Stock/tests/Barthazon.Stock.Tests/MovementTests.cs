using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Services;
using Barthazon.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Stock.Tests;

/// <summary>Jalon S1 : réception, sortie, transfert, correction, annulation, soldes et journal.</summary>
public sealed class MovementTests : IAsyncLifetime
{
    private StockTestApp _app = default!;
    private StockTestData _data = default!;

    private IMovementService Movements => _app.Get<IMovementService>();

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
    public async Task Receiving_six_bars_of_three_meters_brings_the_stock_to_eighteen_meters()
    {
        var bar = await _data.CreateItemAsync("A-0001", "Barre C45 Ø60", StockUnit.Meter, StockItemKind.RawMaterial);

        var receipt = await Movements.ReceiveItemAsync(new ItemMovementInput { ItemId = bar.Id, Quantity = 6 * 3m, Reference = "BL 1234" });

        receipt.IsSuccess.Should().BeTrue();
        receipt.Value.Number.Should().Be("M-000001");
        receipt.Value.Type.Should().Be(StockTransactionType.ItemReceipt);
        receipt.Value.RecordedByName.Should().Be("testeur");
        receipt.Value.Lines.Should().ContainSingle().Which.LocationCode.Should().Be("MAG", "sans emplacement indiqué, l'article entre au magasin");
        (await _data.QuantityAsync(bar.Id)).Should().Be(18m);
    }

    [Fact]
    public async Task An_issue_cannot_exceed_the_stock_unless_negative_item_stock_is_allowed()
    {
        var blank = await _data.CreateItemAsync("A-0002");
        await _data.ReceiveAsync(blank.Id, 5);

        var refused = await Movements.IssueItemAsync(new ItemMovementInput { ItemId = blank.Id, Quantity = 6 });
        refused.IsFailure.Should().BeTrue();
        refused.FirstError!.Message.Should().Contain("seulement 5").And.Contain("correction");

        (await Movements.IssueItemAsync(new ItemMovementInput { ItemId = blank.Id, Quantity = 5 })).IsSuccess.Should().BeTrue();
        (await _data.QuantityAsync(blank.Id)).Should().Be(0);

        await using var permissive = await StockTestApp.CreateAsync(settings: new() { ["Stock:AllowNegativeItemStock"] = "true" });
        var data = new StockTestData(permissive);
        var item = await data.CreateItemAsync("A-0001");
        (await permissive.Get<IMovementService>().IssueItemAsync(new ItemMovementInput { ItemId = item.Id, Quantity = 2 })).IsSuccess.Should().BeTrue();
        (await data.QuantityAsync(item.Id)).Should().Be(-2);
        await permissive.AssertBalancesMatchMovementsAsync();
    }

    [Fact]
    public async Task Quantities_respect_the_unit_and_must_be_positive()
    {
        var piece = await _data.CreateItemAsync("A-0003", unit: StockUnit.Piece);
        var meter = await _data.CreateItemAsync("A-0004", unit: StockUnit.Meter);

        (await Movements.ReceiveItemAsync(new ItemMovementInput { ItemId = piece.Id, Quantity = 1.5m })).IsFailure.Should().BeTrue();
        (await Movements.ReceiveItemAsync(new ItemMovementInput { ItemId = meter.Id, Quantity = 1.2345m })).IsFailure.Should().BeTrue();
        (await Movements.ReceiveItemAsync(new ItemMovementInput { ItemId = meter.Id, Quantity = 0 })).IsFailure.Should().BeTrue();
        (await Movements.ReceiveItemAsync(new ItemMovementInput { ItemId = meter.Id, Quantity = 1.125m })).IsSuccess.Should().BeTrue();
        (await _data.QuantityAsync(meter.Id)).Should().Be(1.125m);
    }

    [Fact]
    public async Task Lot_tracked_items_keep_one_stock_cell_per_lot()
    {
        var bar = await _data.CreateItemAsync("A-0005", unit: StockUnit.Meter, trackLots: true);

        var withoutLot = await Movements.ReceiveItemAsync(new ItemMovementInput { ItemId = bar.Id, Quantity = 3 });
        withoutLot.IsFailure.Should().BeTrue();
        withoutLot.FirstError!.Field.Should().Be(nameof(ItemMovementInput.Lot));

        await _data.ReceiveAsync(bar.Id, 6, lot: "C2345");
        await _data.ReceiveAsync(bar.Id, 3, lot: "C2399");
        var details = (await _data.Items.GetAsync(bar.Id)).Value;
        details.Quantity.Should().Be(9);
        details.Stock.Select(c => (c.Lot, c.Quantity)).Should().BeEquivalentTo([("C2345", 6m), ("C2399", 3m)]);

        (await Movements.IssueItemAsync(new ItemMovementInput { ItemId = bar.Id, Quantity = 4, Lot = "C2399" })).IsFailure.Should().BeTrue("ce lot ne contient que 3 m");
        (await Movements.IssueItemAsync(new ItemMovementInput { ItemId = bar.Id, Quantity = 4, Lot = "C2345" })).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task A_transfer_moves_quantities_between_locations()
    {
        var item = await _data.CreateItemAsync("A-0006");
        var rack = await _data.CreateLocationAsync("MAG-A1");
        await _data.ReceiveAsync(item.Id, 10);
        var cell = (await _data.Items.GetAsync(item.Id)).Value.Stock.Single().Key;

        (await Movements.TransferAsync(new TransferInput { From = cell, ToLocationId = cell.LocationId, Quantity = 1 })).IsFailure.Should().BeTrue();
        (await Movements.TransferAsync(new TransferInput { From = cell, ToLocationId = rack.Id, Quantity = 11 })).IsFailure.Should().BeTrue();
        var moved = await Movements.TransferAsync(new TransferInput { From = cell, ToLocationId = rack.Id, Quantity = 4 });

        moved.IsSuccess.Should().BeTrue();
        moved.Value.Lines.Select(l => (l.LocationCode, l.Delta)).Should().Equal(("MAG", -4m), ("MAG-A1", 4m));
        (await _data.Items.GetAsync(item.Id)).Value.Stock.Select(c => (c.LocationCode, c.Quantity)).Should().BeEquivalentTo([("MAG", 6m), ("MAG-A1", 4m)]);
        (await _data.QuantityAsync(item.Id)).Should().Be(10);
    }

    [Fact]
    public async Task An_adjustment_records_the_difference_with_the_counted_quantity()
    {
        var item = await _data.CreateItemAsync("A-0007");
        await _data.ReceiveAsync(item.Id, 10);
        var cell = (await _data.Items.GetAsync(item.Id)).Value.Stock.Single().Key;

        var adjusted = await Movements.AdjustAsync(new AdjustmentInput { Cell = cell, CountedQuantity = 7, Reason = StockReason.Inventory });
        adjusted.IsSuccess.Should().BeTrue();
        adjusted.Value.Lines.Single().Delta.Should().Be(-3);
        adjusted.Value.Reason.Should().Be(StockReason.Inventory);
        (await Movements.AdjustAsync(new AdjustmentInput { Cell = cell, CountedQuantity = 7 })).IsFailure.Should().BeTrue("aucun écart");

        // Une case vide peut aussi être corrigée (objet trouvé là où on ne l'attendait pas).
        var rack = await _data.CreateLocationAsync("MAG-B2");
        (await Movements.AdjustAsync(new AdjustmentInput { Cell = cell.At(rack.Id), CountedQuantity = 2 })).IsSuccess.Should().BeTrue();
        (await _data.QuantityAsync(item.Id)).Should().Be(9);
    }

    [Fact]
    public async Task A_reversal_cancels_a_movement_which_stays_visible()
    {
        var item = await _data.CreateItemAsync("A-0008");
        var receipt = await _data.ReceiveAsync(item.Id, 10);

        var reversal = await Movements.ReverseAsync(receipt.Id, "erreur de saisie");

        reversal.IsSuccess.Should().BeTrue();
        reversal.Value.Type.Should().Be(StockTransactionType.Reversal);
        reversal.Value.ReversesNumber.Should().Be(receipt.Number);
        reversal.Value.Lines.Single().Delta.Should().Be(-10);
        (await _data.QuantityAsync(item.Id)).Should().Be(0);

        var original = (await Movements.GetAsync(receipt.Id)).Value;
        original.Movement.IsReversed.Should().BeTrue();
        original.Movement.ReversedByNumber.Should().Be(reversal.Value.Number);
        original.CanReverse.Should().BeFalse();
        (await Movements.ReverseAsync(receipt.Id)).FirstError!.Message.Should().Contain("déjà été annulé");
        (await Movements.ReverseAsync(reversal.Value.Id)).FirstError!.Message.Should().Contain("ne s'annule pas");
    }

    [Fact]
    public async Task A_reversal_that_would_make_a_cell_negative_names_the_movement_to_cancel_first()
    {
        var item = await _data.CreateItemAsync("A-0009");
        var receipt = await _data.ReceiveAsync(item.Id, 10);
        var issue = (await Movements.IssueItemAsync(new ItemMovementInput { ItemId = item.Id, Quantity = 8 })).Value;

        var refused = await Movements.ReverseAsync(receipt.Id);

        refused.IsFailure.Should().BeTrue();
        refused.FirstError!.Message.Should().Contain(issue.Number);
        (await Movements.ReverseAsync(issue.Id)).IsSuccess.Should().BeTrue();
        (await Movements.ReverseAsync(receipt.Id)).IsSuccess.Should().BeTrue();
        (await _data.QuantityAsync(item.Id)).Should().Be(0);
    }

    [Fact]
    public async Task The_author_may_undo_their_own_movement_only_within_the_delay()
    {
        var item = await _data.CreateItemAsync("A-0010");
        _app.As(UserRole.Reader, "operateur");
        var first = await _data.ReceiveAsync(item.Id, 5);
        var second = await _data.ReceiveAsync(item.Id, 5);

        _app.Clock.Advance(TimeSpan.FromMinutes(10));
        (await Movements.ReverseAsync(first.Id)).IsSuccess.Should().BeTrue("15 minutes par défaut");

        _app.Clock.Advance(TimeSpan.FromMinutes(10));
        var late = await Movements.ReverseAsync(second.Id);
        late.HasError(ErrorKind.Forbidden).Should().BeTrue();
        late.FirstError!.Message.Should().Contain("15 minutes");

        _app.User.UserId = Guid.NewGuid();
        (await Movements.GetAsync(second.Id)).Value.CanReverse.Should().BeFalse("le mouvement d'un autre utilisateur");
        _app.As(UserRole.Editor);
        (await Movements.ReverseAsync(second.Id)).IsSuccess.Should().BeTrue("un éditeur annule tout mouvement");
    }

    [Fact]
    public async Task Declaring_requires_a_login_and_corrections_require_an_editor()
    {
        var item = await _data.CreateItemAsync("A-0011");
        _app.User.SignOut();
        var anonymous = await Movements.ReceiveItemAsync(new ItemMovementInput { ItemId = item.Id, Quantity = 1 });
        anonymous.HasError(ErrorKind.Unauthenticated).Should().BeTrue();
        anonymous.FirstError!.Message.Should().Contain("nominative");
        (await _data.Items.SearchAsync(new ItemSearchRequest())).IsSuccess.Should().BeTrue("consultation anonyme autorisée");

        _app.As(UserRole.Reader);
        (await Movements.ReceiveItemAsync(new ItemMovementInput { ItemId = item.Id, Quantity = 1 })).IsSuccess.Should().BeTrue("un lecteur connecté déclare");
        var cell = (await _data.Items.GetAsync(item.Id)).Value.Stock.Single().Key;
        (await Movements.AdjustAsync(new AdjustmentInput { Cell = cell, CountedQuantity = 3 })).HasError(ErrorKind.Forbidden).Should().BeTrue();
        (await _data.Items.SaveAsync(null, new ItemInput { Code = "X", Designation = "X" })).HasError(ErrorKind.Forbidden).Should().BeTrue();
        (await _data.Locations.SaveAsync(null, new LocationInput { Code = "X", Name = "X" })).HasError(ErrorKind.Forbidden).Should().BeTrue();
        (await _app.Get<IBalanceVerificationService>().CheckAsync()).HasError(ErrorKind.Forbidden).Should().BeTrue();
    }

    [Fact]
    public async Task Movements_can_be_backdated_within_the_configured_days_but_never_in_the_future()
    {
        var item = await _data.CreateItemAsync("A-0012");
        var now = _app.Clock.Now;

        (await Movements.ReceiveItemAsync(new ItemMovementInput { ItemId = item.Id, Quantity = 1, OccurredAt = now.AddHours(2) })).IsFailure.Should().BeTrue();
        (await Movements.ReceiveItemAsync(new ItemMovementInput { ItemId = item.Id, Quantity = 1, OccurredAt = now.AddDays(-32) })).IsFailure.Should().BeTrue();
        var backdated = await Movements.ReceiveItemAsync(new ItemMovementInput { ItemId = item.Id, Quantity = 1, OccurredAt = now.AddDays(-30) });
        backdated.IsSuccess.Should().BeTrue();
        backdated.Value.OccurredAt.Should().Be(now.AddDays(-30));
        backdated.Value.RecordedAt.Should().Be(now);
    }

    [Fact]
    public async Task The_journal_is_filtered_by_item_type_and_period()
    {
        var a = await _data.CreateItemAsync("A-0013");
        var b = await _data.CreateItemAsync("A-0014");
        await _data.ReceiveAsync(a.Id, 5);
        await _data.ReceiveAsync(b.Id, 5);
        await Movements.IssueItemAsync(new ItemMovementInput { ItemId = a.Id, Quantity = 2 });

        (await Movements.SearchAsync(new MovementSearchRequest { ItemId = a.Id })).Value.Items.Select(m => m.Type)
            .Should().Equal(StockTransactionType.ItemIssue, StockTransactionType.ItemReceipt);
        (await Movements.SearchAsync(new MovementSearchRequest { Type = StockTransactionType.ItemReceipt })).Value.TotalCount.Should().Be(2);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(_app.Clock.Now, _app.Clock.LocalTimeZone).DateTime);
        (await Movements.SearchAsync(new MovementSearchRequest { From = today, To = today })).Value.TotalCount.Should().Be(3);
        (await Movements.SearchAsync(new MovementSearchRequest { To = today.AddDays(-1) })).Value.TotalCount.Should().Be(0);
        (await Movements.GetByNumberAsync("M-000002")).Value.Movement.Lines.Single().SubjectCode.Should().Be("A-0014");
        (await Movements.GetByNumberAsync("3")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Two_simultaneous_issues_never_take_more_than_the_stock()
    {
        var item = await _data.CreateItemAsync("A-0015");
        await _data.ReceiveAsync(item.Id, 10);

        // Deux sessions (deux portées de services) sortent 7 pièces en même temps.
        await using var other = _app.Services.CreateAsyncScope();
        var first = Movements.IssueItemAsync(new ItemMovementInput { ItemId = item.Id, Quantity = 7 });
        var second = other.ServiceProvider.GetRequiredService<IMovementService>().IssueItemAsync(new ItemMovementInput { ItemId = item.Id, Quantity = 7 });
        var results = await Task.WhenAll(first, second);

        results.Count(r => r.IsSuccess).Should().Be(1);
        (await _data.QuantityAsync(item.Id)).Should().Be(3);
    }

    [Fact]
    public async Task Balance_verification_detects_and_repairs_a_corrupted_balance()
    {
        var item = await _data.CreateItemAsync("A-0016");
        await _data.ReceiveAsync(item.Id, 10);
        _app.As(UserRole.Administrator);
        var verification = _app.Get<IBalanceVerificationService>();
        (await verification.CheckAsync()).Value.Should().BeEmpty();

        await using (var context = await _app.Get<Microsoft.EntityFrameworkCore.IDbContextFactory<Infrastructure.Persistence.StockDbContext>>().CreateDbContextAsync())
        {
            var balance = context.Set<StockBalance>().Single();
            balance.Reset(12);
            await context.SaveChangesAsync();
        }

        var discrepancies = (await verification.CheckAsync()).Value;
        discrepancies.Should().ContainSingle().Which.Should().Match<BalanceDiscrepancy>(d => d.RecordedQuantity == 12 && d.ComputedQuantity == 10);
        (await verification.RepairAsync()).Value.Should().Be(1);
        (await _data.QuantityAsync(item.Id)).Should().Be(10);
    }
}
