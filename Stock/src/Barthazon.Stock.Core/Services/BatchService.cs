using System.Globalization;
using Barthazon.Catalog.Contracts;
using Barthazon.Referential.Contracts;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Text;
using Barthazon.Stock.Contracts;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Persistence;
using Barthazon.Stock.Core.Resources;

namespace Barthazon.Stock.Core.Services;

/// <summary>Lancements de fabrication et avancement opération par opération (sections 7.5, 7.6, 8.1 à 8.5, 10.5).</summary>
public interface IBatchService
{
    Task<Result<IReadOnlyList<BatchListItem>>> SearchAsync(BatchSearchRequest request, CancellationToken cancellationToken = default);

    Task<Result<BatchDetails>> GetByNumberAsync(string number, CancellationToken cancellationToken = default);

    Task<Result<BatchDetails>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Aperçu d'un lancement : indice (en vigueur par défaut), gamme qui sera figée, sources et quantités proposées.</summary>
    Task<Result<LaunchPreview>> PreviewLaunchAsync(Guid partId, Guid? revisionId, int quantity, CancellationToken cancellationToken = default);

    /// <summary>Lancement (éditeur) : gamme figée, consommation de la source, pièces en attente de la 1re opération.</summary>
    Task<Result<BatchDetails>> LaunchAsync(LaunchInput input, CancellationToken cancellationToken = default);

    /// <summary>Déclaration d'une opération : bonnes, rebuts (avec motif), opération non réalisée (connecté).</summary>
    Task<Result<BatchDetails>> DeclareAsync(DeclareInput input, CancellationToken cancellationToken = default);

    /// <summary>Renvoi en reprise vers une étape antérieure (connecté).</summary>
    Task<Result<BatchDetails>> ReworkAsync(ReworkInput input, CancellationToken cancellationToken = default);

    /// <summary>Clôture (éditeur) : les pièces encore en cours sont déclarées en rebut.</summary>
    Task<Result<BatchDetails>> CloseAsync(CloseBatchInput input, CancellationToken cancellationToken = default);

    /// <summary>Annulation d'un lancement sans aucune déclaration (éditeur) : la matière revient en stock.</summary>
    Task<Result<BatchDetails>> CancelAsync(Guid batchId, string? comment = null, CancellationToken cancellationToken = default);

    /// <summary>Resynchronise la gamme figée avec la gamme actuelle de l'indice (éditeur), si aucune pièce n'a passé une étape qui change.</summary>
    Task<Result<BatchDetails>> ResyncRoutingAsync(Guid batchId, CancellationToken cancellationToken = default);

    Task<Result<BatchDetails>> UpdateAsync(Guid batchId, BatchEditInput input, CancellationToken cancellationToken = default);

    /// <summary>Pipeline d'une pièce : en-cours par opération, lancement par lancement.</summary>
    Task<Result<PartPipeline>> GetPipelineAsync(Guid partId, CancellationToken cancellationToken = default);
}

internal sealed class BatchService(
    IUnitOfWorkFactory<IStockUnitOfWork> unitOfWorkFactory,
    StockLedger ledger,
    StockAccess access,
    StockOptions options,
    TimeProvider timeProvider,
    IPartLookup parts,
    IPartRoutingLookup routings,
    IMachineLookup machines,
    MovementService movements,
    ITrackedPartService trackedParts) : IBatchService
{
    private const int MaxQuantity = 1_000_000;

    public async Task<Result<IReadOnlyList<BatchListItem>>> SearchAsync(BatchSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<IReadOnlyList<BatchListItem>>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var batches = (await uow.Batches.ListAsync(new BatchFilter
        {
            PartId = request.PartId,
            Statuses = request.Status is { } status ? [status] : null,
        }, cancellationToken))
            .Where(b => SearchNormalizer.Matches(SearchNormalizer.BuildSearchText([b.Number, b.PartReference, b.CustomerOrderReference]), request.Query))
            .ToList();
        var items = await ListItemsAsync(uow, batches, cancellationToken);
        return Result.Success<IReadOnlyList<BatchListItem>>(items
            .OrderBy(b => b.Status)
            .ThenByDescending(b => b.LaunchedOn).ThenByDescending(b => b.Number, StringComparer.Ordinal)
            .ToList());
    }

    public async Task<Result<BatchDetails>> GetByNumberAsync(string number, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(number);
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<BatchDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var batch = await uow.Batches.GetByNumberAsync(number, cancellationToken);
        return batch is null ? Error.NotFound(StockMessages.BatchNotFound) : await DetailsAsync(uow, batch, cancellationToken);
    }

    public async Task<Result<BatchDetails>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<BatchDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var batch = (await uow.Batches.GetManyAsync([id], cancellationToken)).SingleOrDefault();
        return batch is null ? Error.NotFound(StockMessages.BatchNotFound) : await DetailsAsync(uow, batch, cancellationToken);
    }

    public async Task<Result<LaunchPreview>> PreviewLaunchAsync(Guid partId, Guid? revisionId, int quantity, CancellationToken cancellationToken = default)
    {
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<LaunchPreview>.Failure(allowed.Errors);
        }

        var part = await parts.GetAsync(partId, cancellationToken);
        if (part is null)
        {
            return Error.NotFound(StockMessages.PartNotFound);
        }

        var revisions = await routings.GetRevisionsAsync(partId, cancellationToken);
        var revision = revisionId is { } id ? revisions.FirstOrDefault(r => r.Id == id) : revisions.FirstOrDefault(r => r.Status == RevisionStatus.InForce);
        var steps = revision is null ? [] : await FrozenStepsAsync(revision.Id, cancellationToken);

        await using var uow = unitOfWorkFactory.Create();
        var sources = new List<LaunchSourceOption>();
        var details = await trackedParts.GetAsync(partId, cancellationToken);
        if (details.IsSuccess && revision is not null)
        {
            var scoped = details.Value.Sources
                .Where(s => s.RevisionId is null || s.RevisionId == revision.Id)
                .OrderBy(s => s.RevisionId is null ? 1 : 0).ThenByDescending(s => s.IsDefault).ThenBy(s => s.SortOrder);
            foreach (var source in scoped)
            {
                sources.Add(new LaunchSourceOption(source, ProposedConsumption(quantity, source), await SourceCellsAsync(uow, source, cancellationToken)));
            }
        }

        var today = Today();
        var sequence = await uow.Batches.GetLastYearSequenceAsync(today.Year, cancellationToken) + 1;
        return new LaunchPreview(partId, part.InternalReference, part.Designation, revisions, revision, steps, sources, FormatNumber(today, sequence));
    }

    public async Task<Result<BatchDetails>> LaunchAsync(LaunchInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<BatchDetails>.Failure(allowed.Errors);
        }

        ProductionBatch? created = null;
        var recorded = await ledger.RecordAsync(async uow =>
        {
            var validation = new Validation()
                .When(input.Quantity <= 0 || input.Quantity > MaxQuantity, nameof(LaunchInput.Quantity), StockMessages.QuantityMustBePositive)
                .MaxLength(input.CustomerOrderReference, ProductionBatch.OrderReferenceMaxLength, nameof(LaunchInput.CustomerOrderReference), StockMessages.TextTooLong)
                .MaxLength(input.Notes, ProductionBatch.NotesMaxLength, nameof(LaunchInput.Notes), StockMessages.TextTooLong);
            var occurredAt = movements.CheckDate(input.OccurredAt, validation);
            var part = await TrackedPartRegistry.EnsureTrackedAsync(uow, parts, input.PartId, cancellationToken);
            if (part.IsFailure)
            {
                return Result<LedgerEntry>.Failure(part.Errors);
            }

            var revisions = await routings.GetRevisionsAsync(input.PartId, cancellationToken);
            var revision = input.RevisionId is { } revisionId ? revisions.FirstOrDefault(r => r.Id == revisionId) : revisions.FirstOrDefault(r => r.Status == RevisionStatus.InForce);
            if (revision is null)
            {
                return Error.Validation(StockMessages.RevisionRequired, nameof(LaunchInput.RevisionId));
            }

            if (revision.Status == RevisionStatus.Cancelled)
            {
                return Error.Validation(StockMessages.CancelledRevisionLaunch, nameof(LaunchInput.RevisionId));
            }

            if (revision.Status != RevisionStatus.InForce && !input.ConfirmRevisionNotInForce)
            {
                return Error.Validation(string.Format(CultureInfo.CurrentCulture, StockMessages.RevisionNotInForceConfirm, revision.Index), nameof(LaunchInput.ConfirmRevisionNotInForce));
            }

            var location = await ResolveWorkInProgressLocationAsync(uow, input.LocationId, cancellationToken);
            if (location is null)
            {
                validation.Add(nameof(LaunchInput.LocationId), StockMessages.LocationNotFound);
            }

            var steps = await FrozenStepsAsync(revision.Id, cancellationToken);
            var initialStage = 0;
            if (input.IsRecovery && input.RecoveryWaitingOperation is { } waiting)
            {
                var index = steps.ToList().FindIndex(s => s.OperationNumber == waiting);
                if (index < 0)
                {
                    validation.Add(nameof(LaunchInput.RecoveryWaitingOperation), StockMessages.SourceOperationUnknown);
                }

                initialStage = Math.Max(index, 0);
            }

            var consumptions = input.IsRecovery || input.WithoutConsumption ? [] : input.Consumptions.Where(c => c.Quantity != 0).ToList();
            if (!input.IsRecovery && !input.WithoutConsumption)
            {
                await ValidateConsumptionsAsync(uow, part.Value, revision.Id, input.SourceId, consumptions, validation, cancellationToken);
            }

            if (!validation.IsValid)
            {
                return Result<LedgerEntry>.Failure(validation.Errors);
            }

            var launchedOn = LocalDate(occurredAt);
            var sequence = await uow.Batches.GetLastYearSequenceAsync(launchedOn.Year, cancellationToken) + 1;
            var summary = (await parts.GetAsync(input.PartId, cancellationToken))!;
            var batch = new ProductionBatch(FormatNumber(launchedOn, sequence), launchedOn.Year, sequence, input.PartId, revision.Id, summary.InternalReference, revision.Index, input.Quantity, launchedOn)
            {
                DueDate = input.DueDate,
                CustomerOrderReference = Inputs.Clean(input.CustomerOrderReference),
                Notes = Inputs.Clean(input.Notes),
                IsRecovery = input.IsRecovery,
            };
            batch.SetSteps(steps);
            uow.Batches.Add(batch);
            created = batch;

            var entry = new LedgerEntry();
            var launch = entry.Add(movements.NewTransaction(StockTransactionType.Launch, occurredAt));
            launch.BatchId = batch.Id;
            launch.Comment = input.WithoutConsumption ? StockMessages.LaunchWithoutConsumption : null;
            foreach (var consumption in consumptions)
            {
                launch.AddLine(consumption.Cell, -consumption.Quantity);
            }

            launch.AddLine(StockKey.ForWorkInProgress(input.PartId, revision.Id, batch.Id, initialStage, location!.Id), input.Quantity);
            return entry;
        }, cancellationToken);

        return recorded.IsFailure ? Result<BatchDetails>.Failure(recorded.Errors) : await GetAsync(created!.Id, cancellationToken);
    }

    public async Task<Result<BatchDetails>> DeclareAsync(DeclareInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = access.CanDeclare();
        if (allowed.IsFailure)
        {
            return Result<BatchDetails>.Failure(allowed.Errors);
        }

        var recorded = await ledger.RecordAsync(async uow =>
        {
            var batch = await uow.Batches.GetWithStepsAsync(input.BatchId, cancellationToken);
            if (batch is null)
            {
                return Error.NotFound(StockMessages.BatchNotFound);
            }

            var entry = new LedgerEntry();
            var prepared = await PrepareDeclarationAsync(uow, batch, input, entry, cancellationToken);
            return prepared.IsFailure ? Result<LedgerEntry>.Failure(prepared.Errors) : entry;
        }, cancellationToken);

        return recorded.IsFailure ? Result<BatchDetails>.Failure(recorded.Errors) : await GetAsync(input.BatchId, cancellationToken);
    }

    public async Task<Result<BatchDetails>> ReworkAsync(ReworkInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = access.CanDeclare();
        if (allowed.IsFailure)
        {
            return Result<BatchDetails>.Failure(allowed.Errors);
        }

        var recorded = await ledger.RecordAsync(async uow =>
        {
            var batch = await uow.Batches.GetWithStepsAsync(input.BatchId, cancellationToken);
            if (batch is null)
            {
                return Error.NotFound(StockMessages.BatchNotFound);
            }

            var validation = new Validation()
                .When(input.Quantity <= 0, nameof(ReworkInput.Quantity), StockMessages.QuantityMustBePositive)
                .When(input.ToWaitingStep < 1 || input.ToWaitingStep >= input.FromWaitingStep || input.FromWaitingStep > batch.StepCount,
                    nameof(ReworkInput.ToWaitingStep), StockMessages.ReworkStepInvalid)
                .MaxLength(input.Comment, StockTransaction.CommentMaxLength, nameof(ReworkInput.Comment), StockMessages.TextTooLong);
            if (!validation.IsValid)
            {
                return Result<LedgerEntry>.Failure(validation.Errors);
            }

            var fromStage = input.FromWaitingStep - 1;
            var taken = await TakeWorkInProgressAsync(uow, batch, fromStage, input.Quantity, cancellationToken);
            if (taken.IsFailure)
            {
                return Result<LedgerEntry>.Failure(taken.Errors);
            }

            var entry = new LedgerEntry();
            var rework = entry.Add(movements.NewTransaction(StockTransactionType.Rework, timeProvider.GetUtcNow()));
            rework.BatchId = batch.Id;
            rework.StepSequence = input.ToWaitingStep;
            rework.Comment = Inputs.Clean(input.Comment);
            foreach (var (cell, quantity) in taken.Value)
            {
                rework.AddLine(cell, -quantity);
                rework.AddLine(cell with { Stage = input.ToWaitingStep - 1 }, quantity);
            }

            return entry;
        }, cancellationToken);

        return recorded.IsFailure ? Result<BatchDetails>.Failure(recorded.Errors) : await GetAsync(input.BatchId, cancellationToken);
    }

    public async Task<Result<BatchDetails>> CloseAsync(CloseBatchInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<BatchDetails>.Failure(allowed.Errors);
        }

        var recorded = await ledger.RecordAsync(async uow =>
        {
            var batch = await uow.Batches.GetWithStepsAsync(input.BatchId, cancellationToken);
            if (batch is null)
            {
                return Error.NotFound(StockMessages.BatchNotFound);
            }

            if (batch.Status != BatchStatus.InProgress)
            {
                return Error.Validation(StockMessages.BatchNotInProgress);
            }

            var cells = await uow.Balances.ListAsync(new BalanceFilter { Bucket = StockBucket.WorkInProgress, BatchId = batch.Id }, cancellationToken);
            var entry = new LedgerEntry();
            var scrap = entry.Add(movements.NewTransaction(StockTransactionType.Scrap, timeProvider.GetUtcNow()));
            scrap.BatchId = batch.Id;
            scrap.Reason = input.Reason;
            scrap.Comment = Inputs.Clean(input.Comment) ?? StockMessages.ClosedBatchScrap;
            foreach (var cell in cells.Where(c => c.Quantity > 0))
            {
                scrap.AddLine(cell.Key, -cell.Quantity);
            }

            return entry;
        }, cancellationToken);

        return recorded.IsFailure ? Result<BatchDetails>.Failure(recorded.Errors) : await GetAsync(input.BatchId, cancellationToken);
    }

    public async Task<Result<BatchDetails>> CancelAsync(Guid batchId, string? comment = null, CancellationToken cancellationToken = default)
    {
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<BatchDetails>.Failure(allowed.Errors);
        }

        Guid? launchId;
        await using (var uow = unitOfWorkFactory.Create())
        {
            launchId = (await uow.Transactions.ListForBatchAsync(batchId, cancellationToken))
                .FirstOrDefault(t => t.Type == StockTransactionType.Launch && t.BatchId == batchId && !t.IsReversed)?.Id;
        }

        if (launchId is null)
        {
            return Error.Validation(StockMessages.BatchCannotBeCancelled);
        }

        // L'annulation du mouvement de lancement annule le lancement (règle portée par le service des mouvements).
        var reversed = await movements.ReverseAsync(launchId.Value, comment, cancellationToken);
        return reversed.IsFailure ? Result<BatchDetails>.Failure(reversed.Errors) : await GetAsync(batchId, cancellationToken);
    }

    public async Task<Result<BatchDetails>> ResyncRoutingAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<BatchDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var batch = await uow.Batches.GetWithStepsAsync(batchId, cancellationToken);
        if (batch is null)
        {
            return Error.NotFound(StockMessages.BatchNotFound);
        }

        if (batch.Status != BatchStatus.InProgress)
        {
            return Error.Validation(StockMessages.BatchNotInProgress);
        }

        var current = await FrozenStepsAsync(batch.RevisionId, cancellationToken);
        var frozen = batch.Steps.Select(s => s.Definition).ToList();
        var firstDifference = Enumerable.Range(0, Math.Max(frozen.Count, current.Count))
            .FirstOrDefault(i => i >= frozen.Count || i >= current.Count || frozen[i] != current[i], -1);
        if (firstDifference < 0)
        {
            return Error.Validation(StockMessages.RoutingUnchanged);
        }

        // Interdit dès qu'une pièce a passé (ou qu'une déclaration porte sur) une étape qui changerait.
        var changedFromSequence = firstDifference + 1;
        var wip = await uow.Balances.ListAsync(new BalanceFilter { Bucket = StockBucket.WorkInProgress, BatchId = batch.Id }, cancellationToken);
        var declared = (await uow.Transactions.ListForBatchAsync(batch.Id, cancellationToken))
            .Where(t => t.BatchId == batch.Id && !t.IsReversed && t.Type is StockTransactionType.OperationDone or StockTransactionType.OperationSkipped or StockTransactionType.Scrap or StockTransactionType.Rework)
            .Any(t => t.StepSequence >= changedFromSequence);
        if (declared || wip.Any(b => b.Quantity > 0 && (b.Stage >= changedFromSequence || b.Stage >= current.Count)))
        {
            return Error.Validation(string.Format(CultureInfo.CurrentCulture, StockMessages.ResyncForbidden, frozen.ElementAtOrDefault(firstDifference)?.OperationNumber ?? current[firstDifference].OperationNumber));
        }

        batch.SetSteps(current);
        uow.AddAuditNote(nameof(ProductionBatch), batch.Id, "Resync", StockMessages.RoutingResynchronized);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsFailure ? Result<BatchDetails>.Failure(saved.Errors) : await GetAsync(batchId, cancellationToken);
    }

    public async Task<Result<BatchDetails>> UpdateAsync(Guid batchId, BatchEditInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<BatchDetails>.Failure(allowed.Errors);
        }

        var validation = new Validation()
            .MaxLength(input.CustomerOrderReference, ProductionBatch.OrderReferenceMaxLength, nameof(BatchEditInput.CustomerOrderReference), StockMessages.TextTooLong)
            .MaxLength(input.Notes, ProductionBatch.NotesMaxLength, nameof(BatchEditInput.Notes), StockMessages.TextTooLong);
        if (!validation.IsValid)
        {
            return Result<BatchDetails>.Failure(validation.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var batch = await uow.Batches.GetByIdAsync(batchId, cancellationToken);
        if (batch is null)
        {
            return Error.NotFound(StockMessages.BatchNotFound);
        }

        uow.Batches.SetExpectedVersion(batch, input.Version);
        batch.DueDate = input.DueDate;
        batch.CustomerOrderReference = Inputs.Clean(input.CustomerOrderReference);
        batch.Notes = Inputs.Clean(input.Notes);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsFailure ? Result<BatchDetails>.Failure(saved.Errors) : await GetAsync(batchId, cancellationToken);
    }

    public async Task<Result<PartPipeline>> GetPipelineAsync(Guid partId, CancellationToken cancellationToken = default)
    {
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<PartPipeline>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var batches = await uow.Batches.ListAsync(new BatchFilter { PartId = partId, Statuses = [BatchStatus.InProgress] }, cancellationToken);
        var balances = await uow.Balances.ListAsync(new BalanceFilter { SubjectKind = StockSubjectKind.Part, PartId = partId }, cancellationToken);
        var columns = batches.SelectMany(b => b.Steps)
            .GroupBy(s => s.OperationNumber)
            .OrderBy(g => g.Key)
            .Select(g => new PipelineColumn(g.Key, g.First().OperationType))
            .ToList();
        var rows = batches.OrderBy(b => b.Number, StringComparer.Ordinal).Select(batch =>
        {
            var wip = balances.Where(b => b.Bucket == StockBucket.WorkInProgress && b.BatchId == batch.Id).ToList();
            var waiting = batch.Steps.ToDictionary(s => s.OperationNumber, s => (int)wip.Where(b => b.Stage == s.Sequence - 1).Sum(b => b.Quantity));
            var finished = (int)balances.Where(b => b.Bucket == StockBucket.Stock && b.Lot == batch.Number).Sum(b => b.Quantity);
            return new PipelineRow(batch.Id, batch.Number, batch.RevisionIndex, waiting, batch.Steps.ToDictionary(s => s.OperationNumber, s => s.Sequence), finished);
        }).ToList();
        var totals = columns.ToDictionary(c => c.OperationNumber, c => rows.Sum(r => r.Waiting.GetValueOrDefault(c.OperationNumber)));
        var totalFinished = (int)balances.Where(b => b.Bucket == StockBucket.Stock).Sum(b => b.Quantity);
        return new PartPipeline(columns, rows, totals, totalFinished);
    }

    /// <summary>
    /// Prépare la déclaration d'une étape (bonnes + rebuts) dans <paramref name="entry"/>. Utilisée aussi par la saisie
    /// d'une fiche suiveuse complète.
    /// </summary>
    internal async Task<Result> PrepareDeclarationAsync(IStockUnitOfWork uow, ProductionBatch batch, DeclareInput input, LedgerEntry entry, CancellationToken cancellationToken)
    {
        var step = batch.Step(input.StepSequence);
        var validation = new Validation()
            .When(step is null, nameof(DeclareInput.StepSequence), StockMessages.StepNotFound)
            .When(input.GoodQuantity < 0 || input.ScrapQuantity < 0 || input.GoodQuantity + input.ScrapQuantity <= 0, nameof(DeclareInput.GoodQuantity), StockMessages.QuantityMustBePositive)
            .When(input.ScrapQuantity > 0 && input.ScrapReason is null, nameof(DeclareInput.ScrapReason), StockMessages.ScrapReasonRequired)
            .When(batch.Status == BatchStatus.Cancelled, nameof(DeclareInput.BatchId), StockMessages.BatchNotInProgress)
            .MaxLength(input.Comment, StockTransaction.CommentMaxLength, nameof(DeclareInput.Comment), StockMessages.TextTooLong);
        var occurredAt = movements.CheckDate(input.OccurredAt, validation);
        if (!validation.IsValid)
        {
            return Result.Failure(validation.Errors);
        }

        var stage = step!.Sequence - 1;
        var taken = await TakeWorkInProgressAsync(uow, batch, stage, input.GoodQuantity + input.ScrapQuantity, cancellationToken, entry);
        if (taken.IsFailure)
        {
            return taken;
        }

        var isLast = step.Sequence == batch.StepCount;
        Guid? destination = null;
        if (isLast && input.GoodQuantity > 0)
        {
            var location = await ResolveFinishedLocationAsync(uow, batch.PartId, input.DestinationLocationId, cancellationToken);
            if (location is null)
            {
                return Result.Failure(Error.Validation(StockMessages.LocationNotFound, nameof(DeclareInput.DestinationLocationId)));
            }

            destination = location.Id;
        }

        var locations = (await uow.Locations.ListAsync(includeArchived: true, cancellationToken)).ToDictionary(l => l.Id);
        var workshop = await uow.Locations.GetByCodeAsync(options.DefaultWorkInProgressLocationCode, cancellationToken);
        var remainingGood = input.GoodQuantity;
        StockTransaction? done = null;
        StockTransaction? scrap = null;
        foreach (var (cell, quantity) in taken.Value)
        {
            var good = Math.Min(remainingGood, quantity);
            remainingGood -= (int)good;
            var bad = quantity - good;
            if (good > 0)
            {
                done ??= NewStepTransaction(entry, input.Skipped ? StockTransactionType.OperationSkipped : StockTransactionType.OperationDone, batch, step, occurredAt, input.Comment);
                done.AddLine(cell, -good);

                // Retour de sous-traitance : les pièces reviennent à l'atelier ; sinon elles restent où elles sont.
                var next = isLast
                    ? StockKey.ForFinishedPart(batch.PartId, batch.RevisionId, destination!.Value, batch.Number)
                    : cell with
                    {
                        Stage = step.Sequence,
                        LocationId = locations.TryGetValue(cell.LocationId, out var here) && here.Kind == StockLocationKind.Subcontractor && workshop is not null
                            ? workshop.Id
                            : cell.LocationId,
                    };
                done.AddLine(next, good);
            }

            if (bad > 0)
            {
                scrap ??= NewStepTransaction(entry, StockTransactionType.Scrap, batch, step, occurredAt, input.Comment);
                scrap.Reason = input.ScrapReason;
                scrap.AddLine(cell, -bad);
            }
        }

        return Result.Success();
    }

    private StockTransaction NewStepTransaction(LedgerEntry entry, StockTransactionType type, ProductionBatch batch, BatchStep step, DateTimeOffset occurredAt, string? comment)
    {
        var transaction = entry.Add(movements.NewTransaction(type, occurredAt));
        transaction.BatchId = batch.Id;
        transaction.StepSequence = step.Sequence;
        transaction.Comment = Inputs.Clean(comment);
        return transaction;
    }

    /// <summary>
    /// Cases d'en-cours du lancement à l'étape donnée, pour prélever <paramref name="quantity"/> pièces (les plus
    /// grosses cases d'abord). Tient compte des lignes déjà préparées dans <paramref name="pending"/> (fiche suiveuse).
    /// </summary>
    private static async Task<Result<IReadOnlyList<(StockKey Cell, decimal Quantity)>>> TakeWorkInProgressAsync(
        IStockUnitOfWork uow, ProductionBatch batch, int stage, int quantity, CancellationToken cancellationToken, LedgerEntry? pending = null)
    {
        var stored = await uow.Balances.ListAsync(new BalanceFilter { Bucket = StockBucket.WorkInProgress, BatchId = batch.Id }, cancellationToken);
        var available = stored.Where(b => b.Stage == stage).ToDictionary(b => b.Key, b => b.Quantity);
        foreach (var line in pending?.Transactions.SelectMany(t => t.Lines).Where(l => l.Key.IsWorkInProgress && l.Key.BatchId == batch.Id && l.Key.Stage == stage) ?? [])
        {
            available[line.Key] = available.GetValueOrDefault(line.Key) + line.Delta;
        }

        var total = available.Values.Where(q => q > 0).Sum();
        if (total < quantity)
        {
            var waitingFor = batch.Step(stage + 1)?.OperationNumber;
            return Result<IReadOnlyList<(StockKey, decimal)>>.Failure(Error.Validation(waitingFor is null
                ? string.Format(CultureInfo.CurrentCulture, StockMessages.InsufficientWorkInProgress, StockFormat.Quantity(total))
                : string.Format(CultureInfo.CurrentCulture, StockMessages.OnlyWaitingForOperation, StockFormat.Quantity(total), waitingFor)));
        }

        var taken = new List<(StockKey, decimal)>();
        decimal remaining = quantity;
        foreach (var (key, value) in available.Where(a => a.Value > 0).OrderByDescending(a => a.Value))
        {
            var take = Math.Min(remaining, value);
            taken.Add((key, take));
            remaining -= take;
            if (remaining == 0)
            {
                break;
            }
        }

        return taken;
    }

    private async Task ValidateConsumptionsAsync(IStockUnitOfWork uow, TrackedPart part, Guid revisionId, Guid? sourceId, IReadOnlyList<ConsumptionInput> consumptions, Validation validation, CancellationToken cancellationToken)
    {
        var source = part.SourcesFor(revisionId).FirstOrDefault(s => s.Id == sourceId);
        if (source is null)
        {
            validation.Add(nameof(LaunchInput.SourceId), StockMessages.LaunchSourceRequired);
            return;
        }

        if (consumptions.Count == 0 || consumptions.Any(c => c.Quantity <= 0))
        {
            validation.Add(nameof(LaunchInput.Consumptions), StockMessages.ConsumptionRequired);
            return;
        }

        var unit = StockUnit.Piece;
        if (source.Kind == BlankSourceKind.Item && source.ItemId is { } itemId)
        {
            unit = (await uow.Items.GetByIdAsync(itemId, cancellationToken))?.Unit ?? StockUnit.Piece;
        }

        foreach (var consumption in consumptions)
        {
            var cell = consumption.Cell;
            var matches = source.Kind == BlankSourceKind.Item
                ? cell.IsItem && cell.ItemId == source.ItemId
                : source.SourceAfterOperation is null
                    ? cell.IsFinishedPart && cell.PartId == source.SourcePartId
                    : cell.IsWorkInProgress && cell.PartId == source.SourcePartId
                        && await uow.Batches.GetManyAsync([cell.BatchId], cancellationToken) is [{ Status: BatchStatus.InProgress } origin]
                        && origin.StageAfterOperation(source.SourceAfterOperation.Value) == cell.Stage;
            if (!matches)
            {
                validation.Add(nameof(LaunchInput.Consumptions), StockMessages.ConsumptionNotFromSource);
                return;
            }

            if (!StockFormat.FitsUnit(consumption.Quantity, unit))
            {
                validation.Add(nameof(LaunchInput.Consumptions), StockMessages.QuantityPrecision);
                return;
            }
        }
    }

    /// <summary>Cases disponibles d'une source : lot le plus ancien d'abord ; pour un semi-fini, les lancements de la source à la bonne étape.</summary>
    private async Task<IReadOnlyList<SourceCellOption>> SourceCellsAsync(IStockUnitOfWork uow, BlankSourceView source, CancellationToken cancellationToken)
    {
        IReadOnlyList<StockBalance> balances;
        IReadOnlyDictionary<Guid, ProductionBatch> batches = new Dictionary<Guid, ProductionBatch>();
        if (source.Kind == BlankSourceKind.Item)
        {
            balances = await uow.Balances.ListAsync(new BalanceFilter { SubjectKind = StockSubjectKind.Item, ItemId = source.ItemId }, cancellationToken);
        }
        else if (source.SourceAfterOperation is null)
        {
            balances = (await uow.Balances.ListAsync(new BalanceFilter { SubjectKind = StockSubjectKind.Part, Bucket = StockBucket.Stock, PartId = source.SourcePartId }, cancellationToken));
        }
        else
        {
            var all = await uow.Balances.ListAsync(new BalanceFilter { SubjectKind = StockSubjectKind.Part, Bucket = StockBucket.WorkInProgress, PartId = source.SourcePartId }, cancellationToken);
            batches = (await uow.Batches.GetManyAsync(all.Select(b => b.BatchId), cancellationToken)).ToDictionary(b => b.Id);
            balances = all.Where(b => batches.TryGetValue(b.BatchId, out var batch) && batch.Status == BatchStatus.InProgress
                && batch.StageAfterOperation(source.SourceAfterOperation.Value) == b.Stage).ToList();
        }

        var locations = (await uow.Locations.ListAsync(includeArchived: true, cancellationToken)).ToDictionary(l => l.Id);
        return balances.Where(b => b.Quantity > 0)
            .OrderBy(b => b.FirstMovementAt)
            .Select(b => new SourceCellOption(b.Key, locations.TryGetValue(b.LocationId, out var l) ? l.Code : "?", b.Lot.Length == 0 ? null : b.Lot,
                batches.TryGetValue(b.BatchId, out var origin) ? origin.Number : null, b.Quantity, source.Unit))
            .ToList();
    }

    /// <summary>Consommation proposée : quantité × quantité par pièce, arrondie à 3 décimales (à l'unité supérieure pour une pièce).</summary>
    internal static decimal ProposedConsumption(int quantity, BlankSourceView source) =>
        source.Kind == BlankSourceKind.Part
            ? Math.Ceiling(quantity * source.QuantityPerPart)
            : source.Unit == StockUnit.Piece ? Math.Ceiling(StockFormat.Round(quantity * source.QuantityPerPart)) : StockFormat.Round(quantity * source.QuantityPerPart);

    private async Task<IReadOnlyList<BatchStepDefinition>> FrozenStepsAsync(Guid revisionId, CancellationToken cancellationToken)
    {
        var routing = await routings.GetRoutingAsync(revisionId, cancellationToken);
        return routing is null || routing.Operations.Count == 0
            ? [BatchStepDefinition.Default(StockMessages.DefaultStepDescription)]
            : routing.Operations.OrderBy(o => o.Number).Select(BatchStepDefinition.From).ToList();
    }

    private async Task<StockLocation?> ResolveWorkInProgressLocationAsync(IStockUnitOfWork uow, Guid? locationId, CancellationToken cancellationToken)
    {
        var location = locationId is { } id
            ? await uow.Locations.GetByIdAsync(id, cancellationToken)
            : await uow.Locations.GetByCodeAsync(options.DefaultWorkInProgressLocationCode, cancellationToken);
        return location is { IsArchived: false } ? location : null;
    }

    /// <summary>Rangement des pièces finies : demandé, sinon celui de la pièce suivie, sinon le magasin par défaut.</summary>
    private async Task<StockLocation?> ResolveFinishedLocationAsync(IStockUnitOfWork uow, Guid partId, Guid? locationId, CancellationToken cancellationToken)
    {
        var tracked = (await uow.TrackedParts.GetManyAsync([partId], cancellationToken)).SingleOrDefault();
        var location = locationId is { } id
            ? await uow.Locations.GetByIdAsync(id, cancellationToken)
            : tracked?.DefaultLocationId is { } preferred
                ? await uow.Locations.GetByIdAsync(preferred, cancellationToken)
                : await uow.Locations.GetByCodeAsync(options.DefaultStorageLocationCode, cancellationToken);
        return location is { IsArchived: false } ? location : null;
    }

    private async Task<IReadOnlyList<BatchListItem>> ListItemsAsync(IStockUnitOfWork uow, IReadOnlyList<ProductionBatch> batches, CancellationToken cancellationToken)
    {
        if (batches.Count == 0)
        {
            return [];
        }

        var ids = batches.Select(b => b.Id).ToList();
        var summaries = await parts.GetManyAsync(batches.Select(b => b.PartId), cancellationToken);
        var revisionStatus = new Dictionary<Guid, RevisionStatus?>();
        foreach (var partId in batches.Select(b => b.PartId).Distinct())
        {
            foreach (var revision in await routings.GetRevisionsAsync(partId, cancellationToken))
            {
                revisionStatus[revision.Id] = revision.Status;
            }
        }

        var wip = await uow.Balances.ListAsync(new BalanceFilter { Bucket = StockBucket.WorkInProgress, PartIds = batches.Select(b => b.PartId).Distinct().ToList() }, cancellationToken);
        var lastMovements = await uow.Transactions.LastMovementByBatchAsync(ids, cancellationToken);
        var staleBefore = timeProvider.GetUtcNow().AddDays(-options.StaleBatchDays);
        var result = new List<BatchListItem>();
        foreach (var batch in batches)
        {
            var batchWip = wip.Where(b => b.BatchId == batch.Id && b.Quantity > 0).ToList();
            var counters = await CountersAsync(uow, batch, cancellationToken);
            var lowest = batchWip.Count == 0 ? (int?)null : batchWip.Min(b => b.Stage);
            var last = lastMovements.TryGetValue(batch.Id, out var at) ? at : (DateTimeOffset?)null;
            result.Add(new BatchListItem(
                batch.Id, batch.Number, batch.PartId, summaries.GetValueOrDefault(batch.PartId)?.InternalReference ?? batch.PartReference,
                summaries.GetValueOrDefault(batch.PartId)?.Designation, batch.RevisionIndex,
                revisionStatus.GetValueOrDefault(batch.RevisionId) == RevisionStatus.InForce, batch.Status, batch.LaunchedQuantity,
                counters.Finished, counters.Scrapped, (int)batchWip.Sum(b => b.Quantity),
                lowest is { } stage ? batch.Step(stage + 1)?.OperationNumber : null,
                batch.LaunchedOn, batch.DueDate, last, batch.Status == BatchStatus.InProgress && last is { } l && l < staleBefore, batch.IsRecovery));
        }

        return result;
    }

    /// <summary>Pièces finies produites et rebutées par un lancement (mouvements annulés compris : leurs contre-mouvements s'y soustraient).</summary>
    private static async Task<(int Finished, int Scrapped, IReadOnlyList<StockTransaction> Transactions)> CountersAsync(IStockUnitOfWork uow, ProductionBatch batch, CancellationToken cancellationToken)
    {
        var transactions = await uow.Transactions.ListForBatchAsync(batch.Id, cancellationToken);
        var own = transactions.Where(t => t.BatchId == batch.Id).ToList();
        var finished = own.SelectMany(t => t.Lines).Where(l => l.SubjectKind == StockSubjectKind.Part && l.Bucket == StockBucket.Stock && l.PartId == batch.PartId && l.Lot == batch.Number).Sum(l => l.Delta);
        var scrapIds = own.Where(t => t.Type == StockTransactionType.Scrap).Select(t => t.Id).ToHashSet();
        var scrapped = -own.Where(t => t.Type == StockTransactionType.Scrap).SelectMany(t => t.Lines).Sum(l => l.Delta)
            - own.Where(t => t.Type == StockTransactionType.Reversal && t.ReversesTransactionId is { } r && scrapIds.Contains(r)).SelectMany(t => t.Lines).Sum(l => l.Delta);
        return ((int)finished, (int)scrapped, transactions);
    }

    private async Task<Result<BatchDetails>> DetailsAsync(IStockUnitOfWork uow, ProductionBatch batch, CancellationToken cancellationToken)
    {
        var summary = await parts.GetAsync(batch.PartId, cancellationToken);
        var revision = (await routings.GetRevisionsAsync(batch.PartId, cancellationToken)).FirstOrDefault(r => r.Id == batch.RevisionId);
        var wip = await uow.Balances.ListAsync(new BalanceFilter { Bucket = StockBucket.WorkInProgress, BatchId = batch.Id }, cancellationToken);
        var (finished, scrapped, transactions) = await CountersAsync(uow, batch, cancellationToken);
        var names = await StockNames.LoadAsync(uow, parts, wip.Select(b => b.Key).Concat(transactions.SelectMany(t => t.Lines).Select(l => l.Key)), cancellationToken);
        var machineNames = await machines.GetManyAsync(batch.Steps.Where(s => s.MachineId is not null).Select(s => s.MachineId!.Value), cancellationToken);
        var usages = await trackedParts.ListPartsMadeFromPartAsync(batch.PartId, cancellationToken);
        var variants = usages.IsSuccess ? usages.Value : [];

        // Annulations : on retire les quantités des mouvements annulés.
        var effective = transactions.Where(t => t.BatchId == batch.Id && !t.IsReversed && t.Type != StockTransactionType.Reversal).ToList();
        var steps = batch.Steps.Select(step =>
        {
            int Moved(StockTransactionType type) => (int)effective.Where(t => t.Type == type && t.StepSequence == step.Sequence)
                .SelectMany(t => t.Lines).Where(l => l.Delta > 0).Sum(l => l.Delta);
            var scrappedHere = (int)-effective.Where(t => t.Type == StockTransactionType.Scrap && t.StepSequence == step.Sequence)
                .SelectMany(t => t.Lines).Sum(l => l.Delta);
            var waitingCells = wip.Where(b => b.Stage == step.Sequence - 1 && b.Quantity > 0).Select(names.Cell).ToList();
            return new BatchStepView(
                step.Sequence, step.OperationNumber, step.OperationType, step.Description, step.MachineId,
                step.MachineId is { } m && machineNames.TryGetValue(m, out var machine) ? machine.Name : null,
                step.Subcontractor, step.SetupTimeMin, step.CycleTimeMin, step.Sequence == batch.StepCount,
                (int)waitingCells.Sum(c => c.Quantity), Moved(StockTransactionType.OperationDone) + Moved(StockTransactionType.OperationSkipped),
                Moved(StockTransactionType.OperationSkipped), scrappedHere, waitingCells,
                variants.Where(v => v.SourceAfterOperation == step.OperationNumber).ToList());
        }).ToList();

        var consumed = transactions.Where(t => t.Type == StockTransactionType.Launch && t.BatchId == batch.Id && !t.IsReversed)
            .SelectMany(t => t.Lines).Where(l => l.Delta < 0)
            .Select(l => new MovementLineView(l.Key, names.SubjectCode(l.Key), names.SubjectLabel(l.Key), names.Unit(l.Key), names.LocationCode(l.LocationId), names.StageLabel(l.Key), l.Delta))
            .ToList();

        return new BatchDetails(
            batch.Id, batch.Number, batch.PartId, summary?.InternalReference ?? batch.PartReference, summary?.Designation, batch.RevisionId, batch.RevisionIndex,
            revision?.Status, batch.Status, batch.LaunchedQuantity, batch.LaunchedOn, batch.DueDate, batch.CustomerOrderReference, batch.IsRecovery,
            batch.Notes, batch.ClosedOn, batch.Version, finished, scrapped, (int)wip.Sum(b => b.Quantity), steps, consumed);
    }

    private string FormatNumber(DateOnly launchedOn, int sequence) =>
        string.Format(CultureInfo.InvariantCulture, options.BatchNumberPattern, launchedOn.ToDateTime(TimeOnly.MinValue), sequence);

    private DateOnly Today() => LocalDate(timeProvider.GetUtcNow());

    private DateOnly LocalDate(DateTimeOffset at) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, timeProvider.LocalTimeZone).DateTime);
}
