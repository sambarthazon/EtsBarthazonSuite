using Barthazon.Catalog.Contracts;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Stock.Contracts;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Services;
using Barthazon.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Stock.Tests;

/// <summary>Jalon S3 : lancements, gamme figée, déclarations, rebuts, reprises, clôture, orientation (annexe A).</summary>
public sealed class BatchTests : IAsyncLifetime
{
    private static readonly OperationType[] BrideRouting = [OperationType.Cutting, OperationType.Turning, OperationType.Milling, OperationType.Inspection];

    private StockTestApp _app = default!;
    private StockTestData _data = default!;

    private IBatchService Batches => _app.Get<IBatchService>();

    private ITrackedPartService Parts => _app.Get<ITrackedPartService>();

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
    public async Task Annex_A_scenario_from_launch_to_finished_parts()
    {
        var forged = await _data.CreateItemAsync("A-0003", "Brut forgé bride Ø125", kind: StockItemKind.Blank);
        await _data.CreateLocationAsync("MAG-B2");
        var rackA1 = await _data.CreateLocationAsync("MAG-A1");
        await _data.ReceiveAsync(forged.Id, 60, "MAG-B2");
        var left = _app.Catalog.AddPart("P-0012", "Bride Ø120 gauche", "B", BrideRouting);
        var right = _app.Catalog.AddPart("P-0013", "Bride Ø120 droite", "B", BrideRouting);
        foreach (var part in new[] { left, right })
        {
            await Parts.SaveSourceAsync(part.Id, null, new BlankSourceInput { ItemId = forged.Id, QuantityPerPart = 1 });
        }

        // 1. Lancer 30 × P-0012 indice B : gamme 10-20-30-40 figée, A-0003 60 → 30, 30 en attente de l'op 10.
        var batch = await LaunchFromDefaultSourceAsync(left.Id, 30);
        batch.Steps.Select(s => s.OperationNumber).Should().Equal(10, 20, 30, 40);
        batch.RevisionIndex.Should().Be("B");
        batch.Consumed.Should().ContainSingle().Which.Should().Match<MovementLineView>(l => l.SubjectCode == "A-0003" && l.Delta == -30 && l.LocationCode == "MAG-B2");
        (await _data.QuantityAsync(forged.Id)).Should().Be(30);
        Waiting(batch).Should().Equal(30, 0, 0, 0);

        // 2. Op 10 : 30 bonnes. 3. Op 20 : 28 bonnes, 2 rebuts (cote).
        batch = await DeclareAsync(batch, 1, 30);
        Waiting(batch).Should().Equal(0, 30, 0, 0);
        var opTwenty = await DeclareAsync(batch, 2, 28, scrap: 2, reason: StockReason.Dimension);
        Waiting(opTwenty).Should().Equal(0, 0, 28, 0);
        opTwenty.Scrapped.Should().Be(2);

        // 4. L'annuaire ajoute une opération 35 à l'indice B : le lancement garde sa gamme figée.
        _app.Catalog.AddOperation(left.RevisionId, 35, OperationType.Deburring);
        (await Batches.GetAsync(batch.Id)).Value.Steps.Select(s => s.OperationNumber).Should().Equal(10, 20, 30, 40);

        // 5. Op 30 : 20 bonnes. 6. Op 40 : 20 bonnes rangées en MAG-A1.
        batch = await DeclareAsync(batch, 3, 20);
        Waiting(batch).Should().Equal(0, 0, 8, 20);
        batch = await DeclareAsync(batch, 4, 20, destination: rackA1.Id);
        batch.Finished.Should().Be(20);
        batch.InProgress.Should().Be(8);
        var finished = (await Parts.GetAsync(left.Id)).Value.FinishedStock.Should().ContainSingle().Subject;
        finished.Should().Match<StockCellView>(c => c.LocationCode == "MAG-A1" && c.Lot == batch.Number && c.Quantity == 20 && c.Key.RevisionId == left.RevisionId);

        // Pipeline après l'étape 6 : 8 en attente de l'op 30, 20 finies.
        var pipeline = (await Batches.GetPipelineAsync(left.Id)).Value;
        pipeline.Columns.Select(c => c.OperationNumber).Should().Equal(10, 20, 30, 40);
        pipeline.Rows.Should().ContainSingle().Which.Waiting.Should().Equal(new Dictionary<int, int> { [10] = 0, [20] = 0, [30] = 8, [40] = 0 });
        pipeline.Rows.Single().Finished.Should().Be(20);
        pipeline.TotalFinished.Should().Be(20);

        // 7. Lancer 30 × P-0013 : A-0003 30 → 0 ; « 0 bruts = 0 P-0012 ou 0 P-0013 ».
        await LaunchFromDefaultSourceAsync(right.Id, 30);
        (await _data.QuantityAsync(forged.Id)).Should().Be(0);
        (await Parts.ListPartsMadeFromItemAsync(forged.Id)).Value.Select(u => u.Launchable).Should().Equal(0, 0);

        // 8. L'indice C de P-0012 est mis en vigueur : bandeau sur le lancement, pièces finies à un indice remplacé.
        _app.Catalog.PutInForce(_app.Catalog.AddRevision(left.Id, "C").Id);
        (await Batches.GetAsync(batch.Id)).Value.RevisionIsInForce.Should().BeFalse();
        (await Parts.SearchAsync(new TrackedPartSearchRequest { State = TrackedPartStateFilter.SupersededRevision })).Value.Select(p => p.Reference).Should().Equal("P-0012");

        // 10. Annuler la déclaration de l'étape 3 (op 20) : refusé, les pièces ont déjà passé l'op 30.
        var declarations = (await _data.Movements.SearchAsync(new MovementSearchRequest { BatchId = batch.Id, Type = StockTransactionType.OperationDone })).Value.Items;
        var refused = await _data.Movements.ReverseAsync(declarations.Single(m => m.Lines.Any(l => l.StageLabel?.Contains("op 30", StringComparison.Ordinal) == true && l.Delta > 0)).Id);
        refused.IsFailure.Should().BeTrue();
        refused.FirstError!.Message.Should().Contain("ont déjà passé l'op 30");
    }

    [Fact]
    public async Task An_operation_cannot_be_declared_on_more_parts_than_are_waiting()
    {
        var part = _app.Catalog.AddPart("P-0001", operations: BrideRouting);
        var batch = await LaunchWithoutConsumptionAsync(part.Id, 18);

        var refused = await Batches.DeclareAsync(new DeclareInput { BatchId = batch.Id, StepSequence = 2, GoodQuantity = 1 });
        refused.FirstError!.Message.Should().Be("Seulement 0 pièce(s) attendent l'op 20.");
        (await Batches.DeclareAsync(new DeclareInput { BatchId = batch.Id, StepSequence = 1, GoodQuantity = 19 })).FirstError!.Message.Should().Contain("Seulement 18");
        (await Batches.DeclareAsync(new DeclareInput { BatchId = batch.Id, StepSequence = 1, GoodQuantity = 10, ScrapQuantity = 1 })).FirstError!.Field
            .Should().Be(nameof(DeclareInput.ScrapReason));
    }

    [Fact]
    public async Task A_batch_completes_automatically_and_comes_back_when_a_declaration_is_cancelled()
    {
        var part = _app.Catalog.AddPart("P-0002", operations: [OperationType.Turning, OperationType.Inspection]);
        var batch = await LaunchWithoutConsumptionAsync(part.Id, 5);
        batch = await DeclareAsync(batch, 1, 5);
        batch = await DeclareAsync(batch, 2, 4, scrap: 1, reason: StockReason.Aspect);

        batch.Status.Should().Be(BatchStatus.Completed);
        batch.Finished.Should().Be(4);
        batch.Scrapped.Should().Be(1);

        var scrap = (await _data.Movements.SearchAsync(new MovementSearchRequest { BatchId = batch.Id, Type = StockTransactionType.Scrap })).Value.Items.Single();
        (await _data.Movements.ReverseAsync(scrap.Id)).IsSuccess.Should().BeTrue();
        var reopened = (await Batches.GetAsync(batch.Id)).Value;
        reopened.Status.Should().Be(BatchStatus.InProgress);
        reopened.Scrapped.Should().Be(0);
        Waiting(reopened).Should().Equal(0, 1);
    }

    [Fact]
    public async Task Skipped_operation_rework_and_closing()
    {
        var part = _app.Catalog.AddPart("P-0003", operations: BrideRouting);
        var batch = await LaunchWithoutConsumptionAsync(part.Id, 10);
        batch = await DeclareAsync(batch, 1, 10);
        var skipped = await Batches.DeclareAsync(new DeclareInput { BatchId = batch.Id, StepSequence = 2, GoodQuantity = 10, Skipped = true });
        skipped.Value.Steps[1].Skipped.Should().Be(10);
        Waiting(skipped.Value).Should().Equal(0, 0, 10, 0);

        // Reprise : 3 pièces en attente de l'op 30 repartent à l'op 20.
        var rework = await Batches.ReworkAsync(new ReworkInput { BatchId = batch.Id, FromWaitingStep = 3, ToWaitingStep = 2, Quantity = 3 });
        Waiting(rework.Value).Should().Equal(0, 3, 7, 0);
        (await Batches.ReworkAsync(new ReworkInput { BatchId = batch.Id, FromWaitingStep = 2, ToWaitingStep = 3, Quantity = 1 })).IsFailure.Should().BeTrue();

        // Clôture : les 10 pièces encore en cours sont déclarées en rebut.
        _app.As(UserRole.Reader);
        (await Batches.CloseAsync(new CloseBatchInput { BatchId = batch.Id })).HasError(ErrorKind.Forbidden).Should().BeTrue();
        _app.As(UserRole.Editor);
        var closed = await Batches.CloseAsync(new CloseBatchInput { BatchId = batch.Id, Reason = StockReason.Setup });
        closed.Value.Status.Should().Be(BatchStatus.Completed);
        closed.Value.Scrapped.Should().Be(10);
        closed.Value.InProgress.Should().Be(0);
    }

    [Fact]
    public async Task Cancelling_a_batch_without_declarations_returns_the_material()
    {
        var forged = await _data.CreateItemAsync("A-0001");
        await _data.ReceiveAsync(forged.Id, 10);
        var part = _app.Catalog.AddPart("P-0004", operations: BrideRouting);
        await Parts.SaveSourceAsync(part.Id, null, new BlankSourceInput { ItemId = forged.Id, QuantityPerPart = 1 });

        var first = await LaunchFromDefaultSourceAsync(part.Id, 4);
        var cancelled = await Batches.CancelAsync(first.Id);
        cancelled.Value.Status.Should().Be(BatchStatus.Cancelled);
        cancelled.Value.InProgress.Should().Be(0);
        (await _data.QuantityAsync(forged.Id)).Should().Be(10);

        var second = await LaunchFromDefaultSourceAsync(part.Id, 4);
        await DeclareAsync(second, 1, 1);
        (await Batches.CancelAsync(second.Id)).FirstError!.Message.Should().Contain("clôturez");
    }

    [Fact]
    public async Task The_routing_is_frozen_and_can_be_resynchronized_only_before_parts_pass_a_changed_step()
    {
        var part = _app.Catalog.AddPart("P-0005", operations: BrideRouting);
        var batch = await LaunchWithoutConsumptionAsync(part.Id, 10);
        (await Batches.ResyncRoutingAsync(batch.Id)).FirstError!.Message.Should().Contain("identique");

        // Gamme corrigée juste après le lancement : resynchronisation autorisée.
        _app.Catalog.AddOperation(part.RevisionId, 35, OperationType.Deburring);
        var resynced = await Batches.ResyncRoutingAsync(batch.Id);
        resynced.Value.Steps.Select(s => s.OperationNumber).Should().Equal(10, 20, 30, 35, 40);
        Waiting(resynced.Value).Should().Equal(10, 0, 0, 0, 0);

        // Après déclaration des op 10 et 20, un changement de l'op 40 reste possible, pas un changement de l'op 20.
        batch = await DeclareAsync(resynced.Value, 1, 10);
        batch = await DeclareAsync(batch, 2, 10);
        _app.Catalog.RemoveOperation(part.RevisionId, 40);
        (await Batches.ResyncRoutingAsync(batch.Id)).IsSuccess.Should().BeTrue();
        _app.Catalog.RemoveOperation(part.RevisionId, 20);
        (await Batches.ResyncRoutingAsync(batch.Id)).FirstError!.Message.Should().Contain("opération 20");
    }

    [Fact]
    public async Task Launching_an_older_revision_needs_a_confirmation_and_a_cancelled_one_is_refused()
    {
        var part = _app.Catalog.AddPart("P-0006", "Axe", "C", OperationType.Turning);
        var older = _app.Catalog.AddRevision(part.Id, "B", RevisionStatus.Superseded);
        var cancelled = _app.Catalog.AddRevision(part.Id, "A", RevisionStatus.Cancelled);

        var input = new LaunchInput { PartId = part.Id, RevisionId = older.Id, Quantity = 5, WithoutConsumption = true };
        (await Batches.LaunchAsync(input)).FirstError!.Field.Should().Be(nameof(LaunchInput.ConfirmRevisionNotInForce));
        input.ConfirmRevisionNotInForce = true;
        var launched = await Batches.LaunchAsync(input);
        launched.Value.RevisionIndex.Should().Be("B");
        launched.Value.RevisionIsInForce.Should().BeFalse();
        (await Batches.LaunchAsync(new LaunchInput { PartId = part.Id, RevisionId = cancelled.Id, Quantity = 5, WithoutConsumption = true, ConfirmRevisionNotInForce = true }))
            .IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task A_part_without_routing_is_launched_with_a_single_step()
    {
        var part = _app.Catalog.AddPart("P-0007");
        var batch = await LaunchWithoutConsumptionAsync(part.Id, 3);
        batch.Steps.Should().ContainSingle().Which.Should().Match<BatchStepView>(s => s.OperationNumber == 10 && s.OperationType == OperationType.Other && s.Description == "Fabrication");
        (await DeclareAsync(batch, 1, 3)).Status.Should().Be(BatchStatus.Completed);
        (await Parts.GetAsync(part.Id)).Value.Finished.Should().Be(3);
    }

    [Fact]
    public async Task A_common_semi_finished_batch_is_oriented_towards_two_variants()
    {
        var hub = _app.Catalog.AddPart("P-0100", "Moyeu", "A", OperationType.Cutting, OperationType.Turning, OperationType.DrillingTapping, OperationType.Inspection);
        var first = _app.Catalog.AddPart("P-0101", "Moyeu variante 1", "A", OperationType.Milling, OperationType.Inspection);
        var second = _app.Catalog.AddPart("P-0102", "Moyeu variante 2", "A", OperationType.Milling, OperationType.Inspection);
        foreach (var variant in new[] { first, second })
        {
            await Parts.SaveSourceAsync(variant.Id, null, new BlankSourceInput { Kind = BlankSourceKind.Part, SourcePartId = hub.Id, SourceAfterOperation = 20, QuantityPerPart = 1 });
        }

        var common = await LaunchWithoutConsumptionAsync(hub.Id, 50);
        common = await DeclareAsync(common, 1, 50);
        common = await DeclareAsync(common, 2, 50);
        common.Steps[1].Variants.Select(v => v.Reference).Should().Equal("P-0101", "P-0102");
        (await Parts.GetAsync(first.Id)).Value.Sources.Single().Available.Should().Be(50);

        var preview = (await Batches.PreviewLaunchAsync(first.Id, null, 30)).Value;
        var cell = preview.Sources.Single().Cells.Should().ContainSingle().Subject;
        cell.BatchNumber.Should().Be(common.Number);
        cell.Quantity.Should().Be(50);

        var oriented = await LaunchFromDefaultSourceAsync(first.Id, 30);
        Waiting(oriented).Should().Equal(30, 0);
        oriented.Consumed.Single().StageLabel.Should().Contain("attend l'op 30");
        await LaunchFromDefaultSourceAsync(second.Id, 20);

        var done = (await Batches.GetAsync(common.Id)).Value;
        done.InProgress.Should().Be(0);
        done.Status.Should().Be(BatchStatus.Completed);
    }

    [Fact]
    public async Task Batch_numbers_restart_each_year()
    {
        var part = _app.Catalog.AddPart("P-0008");
        _app.Clock.Now = new DateTimeOffset(2026, 12, 31, 10, 0, 0, TimeSpan.Zero);
        (await LaunchWithoutConsumptionAsync(part.Id, 1)).Number.Should().Be("L2026-001");
        (await LaunchWithoutConsumptionAsync(part.Id, 1)).Number.Should().Be("L2026-002");
        _app.Clock.Now = new DateTimeOffset(2027, 1, 2, 10, 0, 0, TimeSpan.Zero);
        (await LaunchWithoutConsumptionAsync(part.Id, 1)).Number.Should().Be("L2027-001");
        (await Batches.GetByNumberAsync("l2026-002")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Operators_declare_but_only_editors_launch_and_nobody_declares_anonymously()
    {
        var part = _app.Catalog.AddPart("P-0009", operations: BrideRouting);
        var batch = await LaunchWithoutConsumptionAsync(part.Id, 5);

        _app.As(UserRole.Reader, "tourneur");
        (await Batches.LaunchAsync(new LaunchInput { PartId = part.Id, Quantity = 1, WithoutConsumption = true })).HasError(ErrorKind.Forbidden).Should().BeTrue();
        (await Batches.DeclareAsync(new DeclareInput { BatchId = batch.Id, StepSequence = 1, GoodQuantity = 5 })).IsSuccess.Should().BeTrue();
        (await Batches.ResyncRoutingAsync(batch.Id)).HasError(ErrorKind.Forbidden).Should().BeTrue();

        _app.User.SignOut();
        (await Batches.DeclareAsync(new DeclareInput { BatchId = batch.Id, StepSequence = 2, GoodQuantity = 5 })).HasError(ErrorKind.Unauthenticated).Should().BeTrue();
        (await Batches.GetAsync(batch.Id)).IsSuccess.Should().BeTrue("consultation anonyme autorisée");
    }

    [Fact]
    public async Task Two_simultaneous_declarations_never_take_more_than_what_waits()
    {
        var part = _app.Catalog.AddPart("P-0010", operations: BrideRouting);
        var batch = await LaunchWithoutConsumptionAsync(part.Id, 30);

        await using var other = _app.Services.CreateAsyncScope();
        var results = await Task.WhenAll(
            Batches.DeclareAsync(new DeclareInput { BatchId = batch.Id, StepSequence = 1, GoodQuantity = 20 }),
            other.ServiceProvider.GetRequiredService<IBatchService>().DeclareAsync(new DeclareInput { BatchId = batch.Id, StepSequence = 1, GoodQuantity = 20 }));

        results.Count(r => r.IsSuccess).Should().Be(1);
        Waiting((await Batches.GetAsync(batch.Id)).Value).Should().Equal(10, 20, 0, 0);
    }

    [Fact]
    public async Task A_recovery_batch_starts_at_a_given_operation_without_consumption()
    {
        var part = _app.Catalog.AddPart("P-0011", operations: BrideRouting);
        var recovery = await Batches.LaunchAsync(new LaunchInput { PartId = part.Id, Quantity = 12, IsRecovery = true, RecoveryWaitingOperation = 30 });

        recovery.Value.IsRecovery.Should().BeTrue();
        recovery.Value.Consumed.Should().BeEmpty();
        Waiting(recovery.Value).Should().Equal(0, 0, 12, 0);
    }

    private static int[] Waiting(BatchDetails batch) => batch.Steps.Select(s => s.Waiting).ToArray();

    private async Task<BatchDetails> LaunchWithoutConsumptionAsync(Guid partId, int quantity)
    {
        var launched = await Batches.LaunchAsync(new LaunchInput { PartId = partId, Quantity = quantity, WithoutConsumption = true });
        launched.IsSuccess.Should().BeTrue(StockTestData.Messages(launched));
        return launched.Value;
    }

    /// <summary>Lance avec la source par défaut et la quantité proposée, prise dans les cases proposées (lot le plus ancien d'abord).</summary>
    private async Task<BatchDetails> LaunchFromDefaultSourceAsync(Guid partId, int quantity)
    {
        var preview = (await Batches.PreviewLaunchAsync(partId, null, quantity)).Value;
        var source = preview.Sources.First();
        var remaining = source.ProposedQuantity;
        var consumptions = new List<ConsumptionInput>();
        foreach (var cell in source.Cells)
        {
            var take = Math.Min(remaining, cell.Quantity);
            if (take > 0)
            {
                consumptions.Add(new ConsumptionInput { Cell = cell.Key, Quantity = take });
                remaining -= take;
            }
        }

        var launched = await Batches.LaunchAsync(new LaunchInput { PartId = partId, Quantity = quantity, SourceId = source.Source.Id, Consumptions = consumptions });
        launched.IsSuccess.Should().BeTrue(StockTestData.Messages(launched));
        return launched.Value;
    }

    private async Task<BatchDetails> DeclareAsync(BatchDetails batch, int step, int good, int scrap = 0, StockReason? reason = null, Guid? destination = null)
    {
        var declared = await Batches.DeclareAsync(new DeclareInput
        {
            BatchId = batch.Id, StepSequence = step, GoodQuantity = good, ScrapQuantity = scrap, ScrapReason = reason, DestinationLocationId = destination,
        });
        declared.IsSuccess.Should().BeTrue(StockTestData.Messages(declared));
        return declared.Value;
    }
}
