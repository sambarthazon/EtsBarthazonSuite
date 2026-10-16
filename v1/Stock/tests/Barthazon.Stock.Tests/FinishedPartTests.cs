using Barthazon.Catalog.Contracts;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Stock.Contracts;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Services;
using Barthazon.Testing;

namespace Barthazon.Stock.Tests;

/// <summary>Jalon S4 : pièces finies par indice et lot, sorties, entrées, avertissements d'indice, contrats du stock.</summary>
public sealed class FinishedPartTests : IAsyncLifetime
{
    private StockTestApp _app = default!;
    private StockTestData _data = default!;
    private FakePart _part = default!;

    private IFinishedPartService Finished => _app.Get<IFinishedPartService>();

    public async Task InitializeAsync()
    {
        _app = await StockTestApp.CreateAsync();
        _data = new StockTestData(_app);
        _part = _app.Catalog.AddPart("P-0012", "Bride Ø120 gauche", "B", OperationType.Turning);
    }

    public async Task DisposeAsync()
    {
        await _app.AssertBalancesMatchMovementsAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Five_parts_left_at_revision_B_after_revision_C_is_put_in_force()
    {
        var batch = (await _app.Get<IBatchService>().LaunchAsync(new LaunchInput { PartId = _part.Id, Quantity = 20, WithoutConsumption = true })).Value;
        await _app.Get<IBatchService>().DeclareAsync(new DeclareInput { BatchId = batch.Id, StepSequence = 1, GoodQuantity = 20 });
        _app.Catalog.PutInForce(_app.Catalog.AddRevision(_part.Id, "C").Id);

        var stock = (await Finished.GetFinishedStockAsync(_part.Id)).Value;
        stock.InForceIndex.Should().Be("C");
        stock.Cells.Should().ContainSingle().Which.Should().Match<FinishedCellOption>(c => c.RevisionIndex == "B" && !c.RevisionIsInForce && c.Lot == batch.Number && c.Quantity == 20);

        var input = new PartShipmentInput { PartId = _part.Id, Quantity = 15, Reference = "BL 4521" };
        var unconfirmed = await Finished.ShipAsync(input);
        unconfirmed.FirstError!.Message.Should().Be("Ces pièces sont à l'indice B, l'indice en vigueur est C : confirmez la sortie.");
        unconfirmed.FirstError.Field.Should().Be(nameof(PartShipmentInput.ConfirmSupersededRevision));

        input.ConfirmSupersededRevision = true;
        var shipped = await Finished.ShipAsync(input);
        shipped.Value.Type.Should().Be(StockTransactionType.Shipment);
        shipped.Value.Reference.Should().Be("BL 4521");
        (await Finished.GetFinishedStockAsync(_part.Id)).Value.Total.Should().Be(5);
    }

    [Fact]
    public async Task Shipments_take_the_revision_in_force_first_then_the_oldest_lot()
    {
        var magasin = await _data.LocationAsync("MAG");
        var revisionA = _app.Catalog.AddRevision(_part.Id, "A", RevisionStatus.Superseded);
        await Finished.ReceiveAsync(new PartReceiptInput { PartId = _part.Id, RevisionId = revisionA.Id, Quantity = 4, Lot = "ANCIEN" });
        await Finished.ReceiveAsync(new PartReceiptInput { PartId = _part.Id, Quantity = 3, Lot = "L2026-001" });
        _app.Clock.Advance(TimeSpan.FromHours(1));
        await Finished.ReceiveAsync(new PartReceiptInput { PartId = _part.Id, Quantity = 5, Lot = "L2026-002" });

        (await Finished.GetFinishedStockAsync(_part.Id)).Value.Cells.Select(c => c.Lot).Should().Equal("L2026-001", "L2026-002", "ANCIEN");
        var shipped = await Finished.ShipAsync(new PartShipmentInput { PartId = _part.Id, Quantity = 6 });
        shipped.IsSuccess.Should().BeTrue("seules des pièces à l'indice en vigueur sortent : pas de confirmation");
        shipped.Value.Lines.Select(l => (l.Lot, l.Delta)).Should().Equal(("L2026-001", -3m), ("L2026-002", -3m));

        var tooMany = await Finished.ShipAsync(new PartShipmentInput { PartId = _part.Id, Quantity = 7, LocationId = magasin.Id });
        tooMany.FirstError!.Message.Should().Be("Seulement 6 pièce(s) finie(s) disponible(s).");
    }

    [Fact]
    public async Task Part_receipt_needs_an_editor_and_shipment_a_login()
    {
        var rack = await _data.CreateLocationAsync("MAG-A1");
        var details = (await _app.Get<ITrackedPartService>().TrackAsync(_part.Id)).Value;
        await _app.Get<ITrackedPartService>().SaveSettingsAsync(_part.Id, new TrackedPartSettingsInput { DefaultLocationId = rack.Id, Version = details.Version });

        _app.As(UserRole.Reader);
        (await Finished.ReceiveAsync(new PartReceiptInput { PartId = _part.Id, Quantity = 2 })).HasError(ErrorKind.Forbidden).Should().BeTrue();
        _app.As(UserRole.Editor);
        var received = await Finished.ReceiveAsync(new PartReceiptInput { PartId = _part.Id, Quantity = 2, Reference = "Retour client" });
        received.Value.Lines.Single().LocationCode.Should().Be("MAG-A1", "rangement habituel de la pièce suivie");

        _app.User.SignOut();
        (await Finished.ShipAsync(new PartShipmentInput { PartId = _part.Id, Quantity = 1 })).HasError(ErrorKind.Unauthenticated).Should().BeTrue();
        _app.As(UserRole.Reader);
        (await Finished.ShipAsync(new PartShipmentInput { PartId = _part.Id, Quantity = 1 })).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Stock_contracts_expose_finished_stock_work_in_progress_batches_and_shipments()
    {
        var batch = (await _app.Get<IBatchService>().LaunchAsync(new LaunchInput { PartId = _part.Id, Quantity = 10, WithoutConsumption = true })).Value;
        await _app.Get<IBatchService>().DeclareAsync(new DeclareInput { BatchId = batch.Id, StepSequence = 1, GoodQuantity = 6 });

        var query = _app.Get<IStockQuery>();
        (await query.GetFinishedStockAsync(_part.Id)).Should().ContainSingle().Which.Should().Match<FinishedStockLine>(l => l.Quantity == 6 && l.RevisionIsInForce && l.LocationCode == "MAG");
        (await query.GetWorkInProgressAsync(_part.Id)).Should().Be(new WorkInProgressSummary(_part.Id, 4, 1));

        var open = await _app.Get<IBatchLookup>().ListOpenAsync();
        open.Should().ContainSingle().Which.Steps.Should().ContainSingle().Which.OperationNumber.Should().Be(10);
        (await _app.Get<IBatchLookup>().GetAsync(batch.Id))!.Status.Should().Be(BatchStatus.InProgress);

        var shipment = await _app.Get<IStockShipments>().RecordShipmentAsync(new ShipmentRequest(_part.Id, null, 4, "MAG", null, "BL 1", null, false));
        shipment.IsSuccess.Should().BeTrue();
        (await query.GetFinishedStockAsync(_part.Id)).Single().Quantity.Should().Be(2);
        (await _app.Get<IStockShipments>().RecordShipmentAsync(new ShipmentRequest(_part.Id, null, 1, "INCONNU", null, null, null, false))).IsFailure.Should().BeTrue();
    }
}
