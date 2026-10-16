using Barthazon.Catalog.Contracts;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Stock.Contracts;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Services;
using Barthazon.Testing;

namespace Barthazon.Stock.Tests;

/// <summary>Jalon S6 : saisie d'une fiche suiveuse au bureau, tableau de bord (alertes, files d'attente par machine).</summary>
public sealed class WorkshopTests : IAsyncLifetime
{
    private StockTestApp _app = default!;
    private StockTestData _data = default!;

    private IBatchService Batches => _app.Get<IBatchService>();

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
    public async Task A_traveler_sheet_back_from_the_workshop_is_entered_at_once_and_backdated()
    {
        var part = _app.Catalog.AddPart("P-0012", operations: [OperationType.Cutting, OperationType.Turning, OperationType.Milling, OperationType.Inspection]);
        var batch = (await Batches.LaunchAsync(new LaunchInput { PartId = part.Id, Quantity = 30, WithoutConsumption = true })).Value;
        var monday = _app.Clock.Now.AddDays(-3);

        var entered = await Batches.DeclareTravelerAsync(new TravelerInput
        {
            BatchId = batch.Id,
            Lines =
            [
                new TravelerLineInput { StepSequence = 1, GoodQuantity = 30, OccurredAt = monday },
                new TravelerLineInput { StepSequence = 2, GoodQuantity = 28, ScrapQuantity = 2, ScrapReason = StockReason.Dimension, OccurredAt = monday.AddHours(3) },
                new TravelerLineInput { StepSequence = 3, GoodQuantity = 20, OccurredAt = monday.AddDays(1) },
                new TravelerLineInput { StepSequence = 4 },
            ],
        });

        entered.IsSuccess.Should().BeTrue(StockTestData.Messages(entered));
        entered.Value.Steps.Select(s => s.Waiting).Should().Equal(0, 0, 8, 20);
        entered.Value.Scrapped.Should().Be(2);
        var movements = (await _data.Movements.SearchAsync(new MovementSearchRequest { BatchId = batch.Id })).Value.Items;
        movements.Where(m => m.Type == StockTransactionType.OperationDone).Select(m => m.OccurredAt).Should().OnlyContain(d => d < _app.Clock.Now.AddDays(-1));
    }

    [Fact]
    public async Task A_traveler_with_an_impossible_line_records_nothing_and_points_to_the_line()
    {
        var part = _app.Catalog.AddPart("P-0013", operations: [OperationType.Cutting, OperationType.Turning]);
        var batch = (await Batches.LaunchAsync(new LaunchInput { PartId = part.Id, Quantity = 30, WithoutConsumption = true })).Value;

        var refused = await Batches.DeclareTravelerAsync(new TravelerInput
        {
            BatchId = batch.Id,
            Lines =
            [
                new TravelerLineInput { StepSequence = 1, GoodQuantity = 30 },
                new TravelerLineInput { StepSequence = 2, GoodQuantity = 31 },
            ],
        });

        refused.IsFailure.Should().BeTrue();
        refused.FirstError!.Field.Should().Be("Lines[1]");
        refused.FirstError.Message.Should().StartWith("Op 20 : Seulement 30");
        (await Batches.GetAsync(batch.Id)).Value.Steps[0].Waiting.Should().Be(30, "tout ou rien");
        (await Batches.DeclareTravelerAsync(new TravelerInput { BatchId = batch.Id, Lines = [new TravelerLineInput { StepSequence = 1 }] })).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task The_dashboard_lists_machine_queues_with_estimated_load_and_alerts()
    {
        var lathe = Guid.NewGuid();
        var part = _app.Catalog.AddPart("P-0020", "Arbre", "A");
        _app.Catalog.AddOperation(part.RevisionId, 10, OperationType.Turning, lathe, setupTimeMin: 30, cycleTimeMin: 4);
        _app.Catalog.AddOperation(part.RevisionId, 20, OperationType.Subcontracting, subcontractor: "Traitement Martin");
        _app.Catalog.AddOperation(part.RevisionId, 30, OperationType.Inspection);
        await Batches.LaunchAsync(new LaunchInput { PartId = part.Id, Quantity = 10, WithoutConsumption = true });
        var second = (await Batches.LaunchAsync(new LaunchInput { PartId = part.Id, Quantity = 5, WithoutConsumption = true })).Value;
        await Batches.DeclareAsync(new DeclareInput { BatchId = second.Id, StepSequence = 1, GoodQuantity = 5 });
        var bar = await _data.CreateItemAsync("A-0001", threshold: 10);
        await _data.ReceiveAsync(bar.Id, 3);

        _app.As(UserRole.Administrator);
        var dashboard = (await _app.Get<IStockDashboardService>().GetAsync()).Value;

        var queue = dashboard.Queues.Single(q => q.MachineId == lathe);
        queue.Quantity.Should().Be(10);
        queue.LoadMinutes.Should().Be(10 * 4 + 30);
        dashboard.Queues.Single(q => q.IsSubcontracting).Quantity.Should().Be(5);
        dashboard.ItemsBelowThreshold.Select(i => i.Code).Should().Equal("A-0001");
        dashboard.BatchesInProgress.Should().HaveCount(2);
        dashboard.InconsistentBalances.Should().Be(0);

        // Lancement à l'arrêt : aucun mouvement depuis Stock:StaleBatchDays (14 jours par défaut).
        _app.Clock.Advance(TimeSpan.FromDays(15));
        (await _app.Get<IStockDashboardService>().GetAsync()).Value.StaleBatches.Should().HaveCount(2);
        _app.Catalog.PutInForce(_app.Catalog.AddRevision(part.Id, "B").Id);
        (await _app.Get<IStockDashboardService>().GetAsync()).Value.BatchesOnSupersededRevision.Should().HaveCount(2);

        _app.As(UserRole.Reader);
        (await _app.Get<IStockDashboardService>().GetAsync()).Value.InconsistentBalances.Should().BeNull("réservé à l'administrateur");
    }
}
