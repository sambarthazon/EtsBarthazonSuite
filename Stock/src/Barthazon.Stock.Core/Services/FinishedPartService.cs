using System.Globalization;
using Barthazon.Catalog.Contracts;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Stock.Contracts;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Persistence;
using Barthazon.Stock.Core.Resources;

namespace Barthazon.Stock.Core.Services;

/// <summary>Case de pièces finies proposée pour une sortie (indice en vigueur d'abord, puis lot le plus ancien).</summary>
public sealed record FinishedCellOption(StockKey Key, string RevisionIndex, bool RevisionIsInForce, string LocationCode, string? Lot, int Quantity);

/// <summary>Pièces finies d'une pièce, prêtes à sortir.</summary>
public sealed record FinishedStock(Guid PartId, string Reference, string Designation, string? InForceIndex, IReadOnlyList<FinishedCellOption> Cells)
{
    public int Total => Cells.Sum(c => c.Quantity);
}

/// <summary>Sortie de pièces finies (livraison).</summary>
public sealed class PartShipmentInput
{
    public Guid PartId { get; set; }

    /// <summary>Cases choisies ; vide = répartition automatique (indice en vigueur d'abord, lot le plus ancien d'abord).</summary>
    public List<ConsumptionInput> Cells { get; set; } = [];

    /// <summary>Quantité pour une répartition automatique.</summary>
    public int Quantity { get; set; }

    public Guid? RevisionId { get; set; }

    public Guid? LocationId { get; set; }

    public string? Lot { get; set; }

    /// <summary>N° de BL client…</summary>
    public string? Reference { get; set; }

    public string? Comment { get; set; }

    public DateTimeOffset? OccurredAt { get; set; }

    /// <summary>Confirme la sortie de pièces à un indice qui n'est plus en vigueur.</summary>
    public bool ConfirmSupersededRevision { get; set; }
}

/// <summary>Entrée de pièces finies hors lancement (retour client, stock initial).</summary>
public sealed class PartReceiptInput
{
    public Guid PartId { get; set; }

    /// <summary>Vide = indice en vigueur.</summary>
    public Guid? RevisionId { get; set; }

    public int Quantity { get; set; }

    /// <summary>Vide = rangement de la pièce suivie, sinon le magasin.</summary>
    public Guid? LocationId { get; set; }

    public string? Lot { get; set; }

    public string? Reference { get; set; }

    public string? Comment { get; set; }

    public DateTimeOffset? OccurredAt { get; set; }
}

/// <summary>Stock de pièces finies : sorties (livraisons) et entrées hors lancement (section 10.3, 10.6).</summary>
public interface IFinishedPartService
{
    Task<Result<FinishedStock>> GetFinishedStockAsync(Guid partId, CancellationToken cancellationToken = default);

    /// <summary>Sortie de pièces finies (connecté). Une sortie à un indice remplacé demande une confirmation.</summary>
    Task<Result<MovementItem>> ShipAsync(PartShipmentInput input, CancellationToken cancellationToken = default);

    /// <summary>Entrée de pièces finies : retour client, stock initial (éditeur).</summary>
    Task<Result<MovementItem>> ReceiveAsync(PartReceiptInput input, CancellationToken cancellationToken = default);
}

internal sealed class FinishedPartService(
    IUnitOfWorkFactory<IStockUnitOfWork> unitOfWorkFactory,
    StockLedger ledger,
    StockAccess access,
    StockOptions options,
    MovementService movements,
    IPartLookup parts,
    IPartRoutingLookup routings) : IFinishedPartService
{
    public async Task<Result<FinishedStock>> GetFinishedStockAsync(Guid partId, CancellationToken cancellationToken = default)
    {
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<FinishedStock>.Failure(allowed.Errors);
        }

        var part = await parts.GetAsync(partId, cancellationToken);
        await using var uow = unitOfWorkFactory.Create();
        var tracked = part is null ? (await uow.TrackedParts.GetManyAsync([partId], cancellationToken)).SingleOrDefault() : null;
        if (part is null && tracked is null)
        {
            return Error.NotFound(StockMessages.PartNotFound);
        }

        var revisions = await routings.GetRevisionsAsync(partId, cancellationToken);
        var cells = await CellsAsync(uow, partId, revisions, cancellationToken);
        return new FinishedStock(partId, part?.InternalReference ?? tracked!.LastKnownReference, part?.Designation ?? tracked!.LastKnownDesignation,
            revisions.FirstOrDefault(r => r.Status == RevisionStatus.InForce)?.Index, cells);
    }

    public async Task<Result<MovementItem>> ShipAsync(PartShipmentInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = access.CanDeclare();
        if (allowed.IsFailure)
        {
            return Result<MovementItem>.Failure(allowed.Errors);
        }

        var recorded = await ledger.RecordAsync(async uow =>
        {
            var validation = new Validation()
                .MaxLength(input.Reference, StockTransaction.ReferenceMaxLength, nameof(PartShipmentInput.Reference), StockMessages.TextTooLong)
                .MaxLength(input.Comment, StockTransaction.CommentMaxLength, nameof(PartShipmentInput.Comment), StockMessages.TextTooLong);
            var occurredAt = movements.CheckDate(input.OccurredAt, validation);
            var tracked = await TrackedPartRegistry.EnsureTrackedAsync(uow, parts, input.PartId, cancellationToken);
            if (tracked.IsFailure)
            {
                return Result<LedgerEntry>.Failure(tracked.Errors);
            }

            var revisions = await routings.GetRevisionsAsync(input.PartId, cancellationToken);
            var available = await CellsAsync(uow, input.PartId, revisions, cancellationToken);
            var allocation = Allocate(input, available, validation);
            if (!validation.IsValid)
            {
                return Result<LedgerEntry>.Failure(validation.Errors);
            }

            // Pièces à un indice remplacé : confirmation explicite (section 8.5).
            var inForce = revisions.FirstOrDefault(r => r.Status == RevisionStatus.InForce);
            var superseded = allocation.Select(a => available.First(c => c.Key == a.Cell)).Where(c => !c.RevisionIsInForce).ToList();
            if (superseded.Count > 0 && !input.ConfirmSupersededRevision)
            {
                return Error.Validation(
                    string.Format(CultureInfo.CurrentCulture, StockMessages.SupersededShipmentConfirm, string.Join(", ", superseded.Select(c => c.RevisionIndex).Distinct()), inForce?.Index ?? "—"),
                    nameof(PartShipmentInput.ConfirmSupersededRevision));
            }

            var entry = new LedgerEntry();
            var shipment = entry.Add(movements.NewTransaction(StockTransactionType.Shipment, occurredAt));
            shipment.Reference = Inputs.Clean(input.Reference);
            shipment.Comment = Inputs.Clean(input.Comment);
            foreach (var (cell, quantity) in allocation)
            {
                shipment.AddLine(cell, -quantity);
            }

            return entry;
        }, cancellationToken);
        return await movements.ToItemAsync(recorded, cancellationToken);
    }

    public async Task<Result<MovementItem>> ReceiveAsync(PartReceiptInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<MovementItem>.Failure(allowed.Errors);
        }

        var recorded = await ledger.RecordAsync(async uow =>
        {
            var validation = new Validation()
                .When(input.Quantity <= 0, nameof(PartReceiptInput.Quantity), StockMessages.QuantityMustBePositive)
                .MaxLength(input.Lot, StockKey.LotMaxLength, nameof(PartReceiptInput.Lot), StockMessages.TextTooLong)
                .MaxLength(input.Reference, StockTransaction.ReferenceMaxLength, nameof(PartReceiptInput.Reference), StockMessages.TextTooLong)
                .MaxLength(input.Comment, StockTransaction.CommentMaxLength, nameof(PartReceiptInput.Comment), StockMessages.TextTooLong);
            var occurredAt = movements.CheckDate(input.OccurredAt, validation);
            var tracked = await TrackedPartRegistry.EnsureTrackedAsync(uow, parts, input.PartId, cancellationToken);
            if (tracked.IsFailure)
            {
                return Result<LedgerEntry>.Failure(tracked.Errors);
            }

            var revisions = await routings.GetRevisionsAsync(input.PartId, cancellationToken);
            var revision = input.RevisionId is { } id ? revisions.FirstOrDefault(r => r.Id == id) : revisions.FirstOrDefault(r => r.Status == RevisionStatus.InForce);
            if (revision is null)
            {
                validation.Add(nameof(PartReceiptInput.RevisionId), StockMessages.RevisionRequired);
            }

            var locationId = input.LocationId ?? tracked.Value.DefaultLocationId;
            var location = locationId is { } l
                ? await uow.Locations.GetByIdAsync(l, cancellationToken)
                : await uow.Locations.GetByCodeAsync(options.DefaultStorageLocationCode, cancellationToken);
            if (location is null || location.IsArchived)
            {
                validation.Add(nameof(PartReceiptInput.LocationId), StockMessages.LocationNotFound);
            }

            if (!validation.IsValid)
            {
                return Result<LedgerEntry>.Failure(validation.Errors);
            }

            var entry = new LedgerEntry();
            var receipt = entry.Add(movements.NewTransaction(StockTransactionType.PartReceipt, occurredAt));
            receipt.Reference = Inputs.Clean(input.Reference);
            receipt.Comment = Inputs.Clean(input.Comment);
            receipt.AddLine(StockKey.ForFinishedPart(input.PartId, revision!.Id, location!.Id, input.Lot), input.Quantity);
            return entry;
        }, cancellationToken);
        return await movements.ToItemAsync(recorded, cancellationToken);
    }

    /// <summary>Cases à sortir : celles choisies, sinon répartition automatique selon les filtres (indice, emplacement, lot).</summary>
    private static IReadOnlyList<(StockKey Cell, decimal Quantity)> Allocate(PartShipmentInput input, IReadOnlyList<FinishedCellOption> available, Validation validation)
    {
        if (input.Cells.Count > 0)
        {
            if (input.Cells.Any(c => c.Quantity <= 0 || c.Quantity != decimal.Truncate(c.Quantity) || !c.Cell.IsFinishedPart || c.Cell.PartId != input.PartId))
            {
                validation.Add(nameof(PartShipmentInput.Cells), StockMessages.QuantityMustBePositive);
            }

            return input.Cells.Select(c => (c.Cell, c.Quantity)).ToList();
        }

        if (input.Quantity <= 0)
        {
            validation.Add(nameof(PartShipmentInput.Quantity), StockMessages.QuantityMustBePositive);
            return [];
        }

        var lot = StockKey.NormalizeLot(input.Lot);
        var candidates = available
            .Where(c => input.RevisionId is null || c.Key.RevisionId == input.RevisionId)
            .Where(c => input.LocationId is null || c.Key.LocationId == input.LocationId)
            .Where(c => lot.Length == 0 || c.Key.Lot == lot)
            .ToList();
        if (candidates.Sum(c => c.Quantity) < input.Quantity)
        {
            validation.Add(nameof(PartShipmentInput.Quantity),
                string.Format(CultureInfo.CurrentCulture, StockMessages.InsufficientFinishedParts, candidates.Sum(c => c.Quantity)));
            return [];
        }

        var allocation = new List<(StockKey, decimal)>();
        var remaining = input.Quantity;
        foreach (var cell in candidates)
        {
            var take = Math.Min(remaining, cell.Quantity);
            allocation.Add((cell.Key, take));
            remaining -= take;
            if (remaining == 0)
            {
                break;
            }
        }

        return allocation;
    }

    /// <summary>Pièces finies en stock : indice en vigueur d'abord, puis la plus ancienne entrée.</summary>
    private static async Task<IReadOnlyList<FinishedCellOption>> CellsAsync(IStockUnitOfWork uow, Guid partId, IReadOnlyList<PartRevisionSummary> revisions, CancellationToken cancellationToken)
    {
        var balances = await uow.Balances.ListAsync(new BalanceFilter { SubjectKind = StockSubjectKind.Part, Bucket = StockBucket.Stock, PartId = partId }, cancellationToken);
        var locations = (await uow.Locations.ListAsync(includeArchived: true, cancellationToken)).ToDictionary(l => l.Id);
        return balances.Where(b => b.Quantity > 0)
            .Select(b =>
            {
                var revision = revisions.FirstOrDefault(r => r.Id == b.RevisionId);
                return (Balance: b, Option: new FinishedCellOption(b.Key, revision?.Index ?? "?", revision?.Status == RevisionStatus.InForce,
                    locations.TryGetValue(b.LocationId, out var location) ? location.Code : "?", b.Lot.Length == 0 ? null : b.Lot, (int)b.Quantity));
            })
            .OrderBy(x => x.Option.RevisionIsInForce ? 0 : 1)
            .ThenBy(x => x.Balance.FirstMovementAt)
            .Select(x => x.Option)
            .ToList();
    }
}

/// <summary>Contrats exposés aux autres applications (section 6.5), au-dessus des services du stock.</summary>
internal sealed class StockContractsService(
    IUnitOfWorkFactory<IStockUnitOfWork> unitOfWorkFactory,
    StockAccess access,
    IFinishedPartService finishedParts) : IStockQuery, IBatchLookup, IStockShipments
{
    public async Task<IReadOnlyList<FinishedStockLine>> GetFinishedStockAsync(Guid partId, CancellationToken cancellationToken = default)
    {
        var stock = await finishedParts.GetFinishedStockAsync(partId, cancellationToken);
        return stock.IsFailure
            ? []
            : stock.Value.Cells.Select(c => new FinishedStockLine(partId, c.Key.RevisionId, c.RevisionIndex, c.RevisionIsInForce, c.LocationCode, c.Lot, c.Quantity)).ToList();
    }

    public async Task<WorkInProgressSummary> GetWorkInProgressAsync(Guid partId, CancellationToken cancellationToken = default)
    {
        if (access.CanRead().IsFailure)
        {
            return new WorkInProgressSummary(partId, 0, 0);
        }

        await using var uow = unitOfWorkFactory.Create();
        var wip = (await uow.Balances.ListAsync(new BalanceFilter { SubjectKind = StockSubjectKind.Part, Bucket = StockBucket.WorkInProgress, PartId = partId }, cancellationToken))
            .Where(b => b.Quantity > 0).ToList();
        return new WorkInProgressSummary(partId, (int)wip.Sum(b => b.Quantity), wip.Select(b => b.BatchId).Distinct().Count());
    }

    public async Task<BatchSummary?> GetAsync(Guid batchId, CancellationToken cancellationToken = default)
    {
        if (access.CanRead().IsFailure)
        {
            return null;
        }

        await using var uow = unitOfWorkFactory.Create();
        var batch = (await uow.Batches.GetManyAsync([batchId], cancellationToken)).SingleOrDefault();
        return batch is null ? null : ToSummary(batch);
    }

    public async Task<IReadOnlyList<BatchSummary>> ListOpenAsync(CancellationToken cancellationToken = default)
    {
        if (access.CanRead().IsFailure)
        {
            return [];
        }

        await using var uow = unitOfWorkFactory.Create();
        var batches = await uow.Batches.ListAsync(new BatchFilter { Statuses = [BatchStatus.InProgress] }, cancellationToken);
        return batches.OrderBy(b => b.Number, StringComparer.Ordinal).Select(ToSummary).ToList();
    }

    public async Task<Result<Guid>> RecordShipmentAsync(ShipmentRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        Guid? locationId = null;
        if (request.LocationCode is { } code)
        {
            await using var uow = unitOfWorkFactory.Create();
            locationId = (await uow.Locations.GetByCodeAsync(code, cancellationToken))?.Id;
            if (locationId is null)
            {
                return Error.Validation(StockMessages.LocationNotFound, nameof(ShipmentRequest.LocationCode));
            }
        }

        var shipped = await finishedParts.ShipAsync(new PartShipmentInput
        {
            PartId = request.PartId, RevisionId = request.RevisionId, Quantity = request.Quantity, LocationId = locationId, Lot = request.Lot,
            Reference = request.Reference, OccurredAt = request.OccurredAt, ConfirmSupersededRevision = request.ConfirmSupersededRevision,
        }, cancellationToken);
        return shipped.IsSuccess ? shipped.Value.Id : Result<Guid>.Failure(shipped.Errors);
    }

    private static BatchSummary ToSummary(ProductionBatch batch) => new(
        batch.Id, batch.Number, batch.PartId, batch.RevisionId, batch.Status, batch.LaunchedQuantity,
        batch.Steps.Select(s => new BatchStepSummary(s.Id, s.Sequence, s.OperationNumber, s.OperationType, s.MachineId, s.Description)).ToList());
}
