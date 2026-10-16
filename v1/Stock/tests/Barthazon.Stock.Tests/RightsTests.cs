using Barthazon.Catalog.Contracts;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Services;
using Barthazon.Testing;

namespace Barthazon.Stock.Tests;

/// <summary>Droits du stock, ligne par ligne du tableau de la section 11 (vérifiés dans les services).</summary>
public sealed class RightsTests : IAsyncLifetime
{
    private StockTestApp _app = default!;
    private StockTestData _data = default!;
    private FakePart _part = default!;
    private Guid _itemId;
    private Guid _batchId;
    private Guid _workshopId;

    private IBatchService Batches => _app.Get<IBatchService>();

    private IMovementService Movements => _app.Get<IMovementService>();

    public async Task InitializeAsync()
    {
        _app = await StockTestApp.CreateAsync();
        _data = new StockTestData(_app);
        _part = _app.Catalog.AddPart("P-0001", operations: [OperationType.Cutting, OperationType.Turning]);
        _itemId = (await _data.CreateItemAsync("A-0001", "Débit")).Id;
        await _data.ReceiveAsync(_itemId, 50, "MAG");
        _batchId = (await Batches.LaunchAsync(new LaunchInput { PartId = _part.Id, Quantity = 20, WithoutConsumption = true })).Value.Id;
        await Batches.DeclareAsync(new DeclareInput { BatchId = _batchId, StepSequence = 1, GoodQuantity = 20 });
        await Batches.DeclareAsync(new DeclareInput { BatchId = _batchId, StepSequence = 2, GoodQuantity = 5 });
        _workshopId = (await _data.LocationAsync("ATELIER")).Id;
    }

    public async Task DisposeAsync()
    {
        await _app.AssertBalancesMatchMovementsAsync();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Consulting_follows_anonymous_read()
    {
        _app.User.SignOut();
        (await _app.Get<IItemService>().SearchAsync(new ItemSearchRequest())).IsSuccess.Should().BeTrue("la consultation anonyme est autorisée par défaut");

        await using var closed = await StockTestApp.CreateAsync(settings: new() { ["Security:AllowAnonymousRead"] = "false" });
        closed.User.SignOut();
        (await closed.Get<IItemService>().SearchAsync(new ItemSearchRequest())).IsFailure.Should().BeTrue();
        (await closed.Get<IBatchService>().SearchAsync(new BatchSearchRequest())).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Declaring_requires_a_login_but_any_role_and_is_refused_to_an_anonymous_user()
    {
        _app.User.SignOut();
        foreach (var (name, result) in await DeclarationsAsync())
        {
            result.HasError(ErrorKind.Unauthenticated).Should().BeTrue($"{name} est refusé sans connexion");
        }

        _app.As(UserRole.Reader, "operateur");
        foreach (var (name, result) in await DeclarationsAsync())
        {
            result.IsSuccess.Should().BeTrue($"{name} est permis au rôle Lecteur : {StockTestData.Messages(result)}");
        }
    }

    [Fact]
    public async Task A_reader_cancels_only_their_own_movement_within_the_delay_and_an_editor_cancels_any()
    {
        _app.As(UserRole.Reader, "operateur");
        var own = (await _data.Movements.IssueItemAsync(new ItemMovementInput { ItemId = _itemId, Quantity = 1 })).Value;
        var other = (await _data.Movements.IssueItemAsync(new ItemMovementInput { ItemId = _itemId, Quantity = 1 })).Value;
        var late = (await _data.Movements.IssueItemAsync(new ItemMovementInput { ItemId = _itemId, Quantity = 1 })).Value;
        (await Movements.ReverseAsync(own.Id)).IsSuccess.Should().BeTrue();
        _app.Clock.Advance(TimeSpan.FromMinutes(20));
        (await Movements.ReverseAsync(late.Id)).HasError(ErrorKind.Forbidden).Should().BeTrue("le délai d'annulation de son propre mouvement est dépassé");

        _app.As(UserRole.Reader, "autre-operateur");
        _app.User.UserId = Guid.NewGuid();
        (await Movements.ReverseAsync(other.Id)).HasError(ErrorKind.Forbidden).Should().BeTrue("ce n'est pas son mouvement");

        _app.As(UserRole.Editor, "chef");
        (await Movements.ReverseAsync(other.Id)).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Launching_closing_resynchronising_and_receiving_finished_parts_require_an_editor()
    {
        _app.As(UserRole.Reader, "operateur");
        (await Batches.LaunchAsync(new LaunchInput { PartId = _part.Id, Quantity = 5, WithoutConsumption = true })).HasError(ErrorKind.Forbidden).Should().BeTrue();
        (await Batches.CloseAsync(new CloseBatchInput { BatchId = _batchId })).HasError(ErrorKind.Forbidden).Should().BeTrue();
        (await Batches.ResyncRoutingAsync(_batchId)).HasError(ErrorKind.Forbidden).Should().BeTrue();
        (await Batches.CancelAsync(_batchId)).IsFailure.Should().BeTrue();
        (await _app.Get<IFinishedPartService>().ReceiveAsync(new PartReceiptInput { PartId = _part.Id, Quantity = 1 })).HasError(ErrorKind.Forbidden).Should().BeTrue();

        _app.As(UserRole.Editor, "chef");
        (await _app.Get<IFinishedPartService>().ReceiveAsync(new PartReceiptInput { PartId = _part.Id, Quantity = 1 })).IsSuccess.Should().BeTrue();
        (await Batches.CloseAsync(new CloseBatchInput { BatchId = _batchId })).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Settings_corrections_inventories_and_imports_require_an_editor()
    {
        _app.As(UserRole.Reader, "operateur");
        var cell = StockKey.ForItem(_itemId, (await _data.LocationAsync("MAG")).Id, null);
        (await _app.Get<IItemService>().SaveAsync(null, new ItemInput { Code = "A-0099", Designation = "X" })).HasError(ErrorKind.Forbidden).Should().BeTrue();
        (await _app.Get<ILocationService>().SaveAsync(null, new LocationInput { Code = "R1", Name = "R1" })).HasError(ErrorKind.Forbidden).Should().BeTrue();
        (await _app.Get<ITrackedPartService>().TrackAsync(_part.Id)).HasError(ErrorKind.Forbidden).Should().BeTrue();
        (await _app.Get<ITrackedPartService>().SaveSourceAsync(_part.Id, null, new BlankSourceInput { ItemId = _itemId })).HasError(ErrorKind.Forbidden).Should().BeTrue();
        (await Movements.AdjustAsync(new AdjustmentInput { Cell = cell, CountedQuantity = 3 })).HasError(ErrorKind.Forbidden).Should().BeTrue();
        (await _app.Get<IInventoryService>().OpenAsync(new OpenInventoryInput())).HasError(ErrorKind.Forbidden).Should().BeTrue();
        (await _app.Get<IStockImportService>().AnalyzeAsync(new MemoryStream(_app.Get<IStockImportService>().CreateTemplate().Content), "stock.xlsx"))
            .HasError(ErrorKind.Forbidden).Should().BeTrue();

        _app.As(UserRole.Editor, "chef");
        (await Movements.AdjustAsync(new AdjustmentInput { Cell = cell, CountedQuantity = 48 })).IsSuccess.Should().BeTrue();
        (await _app.Get<IInventoryService>().OpenAsync(new OpenInventoryInput())).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Verifying_and_repairing_balances_require_an_administrator()
    {
        var verification = _app.Get<IBalanceVerificationService>();
        _app.As(UserRole.Editor, "chef");
        (await verification.CheckAsync()).HasError(ErrorKind.Forbidden).Should().BeTrue();
        (await verification.RepairAsync()).HasError(ErrorKind.Forbidden).Should().BeTrue();

        _app.As(UserRole.Administrator, "admin");
        (await verification.CheckAsync()).Value.Should().BeEmpty();
    }

    /// <summary>Une déclaration de chaque sorte (opération, rebut, reprise, réception, sortie, transfert, livraison).</summary>
    private async Task<IReadOnlyList<(string Name, Result Result)>> DeclarationsAsync()
    {
        var waiting = StockKey.ForWorkInProgress(_part.Id, _part.RevisionId, _batchId, 1, _workshopId);
        var results = new List<(string, Result)>
        {
            ("Opération", await Batches.DeclareAsync(new DeclareInput { BatchId = _batchId, StepSequence = 2, GoodQuantity = 1 })),
            ("Rebut", await Batches.DeclareAsync(new DeclareInput { BatchId = _batchId, StepSequence = 2, ScrapQuantity = 1, ScrapReason = StockReason.Aspect })),
            ("Reprise", await Batches.ReworkAsync(new ReworkInput { BatchId = _batchId, FromWaitingStep = 2, ToWaitingStep = 1, Quantity = 1 })),
            ("Réception", await Movements.ReceiveItemAsync(new ItemMovementInput { ItemId = _itemId, Quantity = 2 })),
            ("Sortie", await Movements.IssueItemAsync(new ItemMovementInput { ItemId = _itemId, Quantity = 1 })),
            ("Transfert", await Movements.TransferAsync(new TransferInput { From = waiting, ToLocationId = (await _data.LocationAsync("EXPE")).Id, Quantity = 1 })),
            ("Livraison", await _app.Get<IFinishedPartService>().ShipAsync(new PartShipmentInput { PartId = _part.Id, Quantity = 1 })),
        };
        return results;
    }
}
