using Barthazon.Catalog.Contracts;
using Barthazon.Referential.Contracts;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Text;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Persistence;
using Barthazon.Stock.Core.Resources;

namespace Barthazon.Stock.Core.Services;

/// <summary>Pièces suivies en stock et leurs sources de brut (sections 7.3, 7.4, 10.2, 10.3).</summary>
public interface ITrackedPartService
{
    Task<Result<IReadOnlyList<TrackedPartListItem>>> SearchAsync(TrackedPartSearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>Fiche stock d'une pièce par sa référence (suivie ou non ; une pièce disparue de l'annuaire reste lisible).</summary>
    Task<Result<TrackedPartDetails>> GetByReferenceAsync(string reference, CancellationToken cancellationToken = default);

    Task<Result<TrackedPartDetails>> GetAsync(Guid partId, CancellationToken cancellationToken = default);

    /// <summary>« Suivre cette pièce en stock » (éditeur). Sans effet si elle est déjà suivie.</summary>
    Task<Result<TrackedPartDetails>> TrackAsync(Guid partId, CancellationToken cancellationToken = default);

    Task<Result<TrackedPartDetails>> SaveSettingsAsync(Guid partId, TrackedPartSettingsInput input, CancellationToken cancellationToken = default);

    /// <summary>Ajoute (<paramref name="sourceId"/> vide) ou modifie une source de brut (éditeur).</summary>
    Task<Result<TrackedPartDetails>> SaveSourceAsync(Guid partId, Guid? sourceId, BlankSourceInput input, CancellationToken cancellationToken = default);

    Task<Result<TrackedPartDetails>> RemoveSourceAsync(Guid partId, Guid sourceId, CancellationToken cancellationToken = default);

    /// <summary>Pièces qui déclarent cet article comme source (« pièces issues de ce brut »).</summary>
    Task<Result<IReadOnlyList<SourceUsage>>> ListPartsMadeFromItemAsync(Guid itemId, CancellationToken cancellationToken = default);

    /// <summary>Pièces qui déclarent cette pièce comme source (variantes d'un semi-fini commun…).</summary>
    Task<Result<IReadOnlyList<SourceUsage>>> ListPartsMadeFromPartAsync(Guid partId, CancellationToken cancellationToken = default);
}

internal sealed class TrackedPartService(
    IUnitOfWorkFactory<IStockUnitOfWork> unitOfWorkFactory,
    StockAccess access,
    IPartLookup parts,
    IPartRoutingLookup routings,
    ICustomerLookup customers,
    IMaterialLookup materials) : ITrackedPartService
{
    /// <summary>Précision de la quantité par pièce (1 brut donne 3 pièces = 0,333333).</summary>
    private const int QuantityPerPartDecimals = 6;

    public async Task<Result<IReadOnlyList<TrackedPartListItem>>> SearchAsync(TrackedPartSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<IReadOnlyList<TrackedPartListItem>>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var tracked = await uow.TrackedParts.ListAsync(cancellationToken);
        var partIds = tracked.Select(t => t.PartId).ToList();
        var summaries = await parts.GetManyAsync(partIds, cancellationToken);
        var balances = partIds.Count == 0 ? [] : await uow.Balances.ListAsync(new BalanceFilter { SubjectKind = StockSubjectKind.Part, PartIds = partIds }, cancellationToken);
        var byPart = balances.ToLookup(b => b.PartId);
        var owners = await customers.GetManyAsync(summaries.Values.Select(p => p.CustomerId).Distinct(), cancellationToken);

        var items = new List<TrackedPartListItem>();
        foreach (var part in tracked)
        {
            summaries.TryGetValue(part.PartId, out var summary);
            var reference = summary?.InternalReference ?? part.LastKnownReference;
            var designation = summary?.Designation ?? part.LastKnownDesignation;
            if (!SearchNormalizer.Matches(SearchNormalizer.BuildSearchText([reference, designation, summary?.CustomerReference]), request.Query)
                || (request.CustomerId is { } customerId && summary?.CustomerId != customerId))
            {
                continue;
            }

            var partBalances = byPart[part.PartId].ToList();
            if (request.LocationId is { } locationId && partBalances.All(b => b.LocationId != locationId))
            {
                continue;
            }

            var finished = (int)partBalances.Where(b => b.Bucket == StockBucket.Stock).Sum(b => b.Quantity);
            var inProgress = (int)partBalances.Where(b => b.Bucket == StockBucket.WorkInProgress).Sum(b => b.Quantity);
            var superseded = await HasSupersededStockAsync(part.PartId, partBalances, cancellationToken);
            var item = new TrackedPartListItem(
                part.PartId, reference, designation, summary?.CustomerId,
                summary is not null && owners.TryGetValue(summary.CustomerId, out var owner) ? owner.Display : null,
                summary is null, summary?.Status, summary?.IsArchived ?? false,
                summary?.CurrentRevisionStatus == RevisionStatus.InForce ? summary.CurrentRevisionIndex : null,
                finished, inProgress, part.MinimumThreshold, superseded);
            var keep = request.State switch
            {
                TrackedPartStateFilter.Ok => !item.IsBelowThreshold && !item.IsOutOfStock && !item.HasSupersededStock,
                TrackedPartStateFilter.BelowThreshold => item.IsBelowThreshold,
                TrackedPartStateFilter.OutOfStock => item.IsOutOfStock,
                TrackedPartStateFilter.SupersededRevision => item.HasSupersededStock,
                _ => true,
            };
            if (keep && (!request.WithWorkInProgressOnly || inProgress > 0))
            {
                items.Add(item);
            }
        }

        return Result.Success<IReadOnlyList<TrackedPartListItem>>(items.OrderBy(i => i.Reference, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    public async Task<Result<TrackedPartDetails>> GetByReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reference);
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<TrackedPartDetails>.Failure(allowed.Errors);
        }

        var summary = await parts.GetByReferenceAsync(reference, cancellationToken);
        await using var uow = unitOfWorkFactory.Create();
        if (summary is not null)
        {
            return await DetailsAsync(uow, summary.Id, cancellationToken);
        }

        var known = await uow.TrackedParts.FindByLastKnownReferenceAsync(reference, cancellationToken);
        return known is null ? Error.NotFound(StockMessages.PartNotFound) : await DetailsAsync(uow, known.PartId, cancellationToken);
    }

    public async Task<Result<TrackedPartDetails>> GetAsync(Guid partId, CancellationToken cancellationToken = default)
    {
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<TrackedPartDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        return await DetailsAsync(uow, partId, cancellationToken);
    }

    public async Task<Result<TrackedPartDetails>> TrackAsync(Guid partId, CancellationToken cancellationToken = default)
    {
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<TrackedPartDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var tracked = await TrackedPartRegistry.EnsureTrackedAsync(uow, parts, partId, cancellationToken);
        if (tracked.IsFailure)
        {
            return Result<TrackedPartDetails>.Failure(tracked.Errors);
        }

        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? await ReloadAsync(partId, cancellationToken) : Result<TrackedPartDetails>.Failure(saved.Errors);
    }

    public async Task<Result<TrackedPartDetails>> SaveSettingsAsync(Guid partId, TrackedPartSettingsInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<TrackedPartDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var validation = new Validation()
            .When(input.MinimumThreshold is < 0, nameof(TrackedPartSettingsInput.MinimumThreshold), StockMessages.MustBePositive)
            .MaxLength(input.Notes, TrackedPart.NotesMaxLength, nameof(TrackedPartSettingsInput.Notes), StockMessages.TextTooLong);
        if (input.DefaultLocationId is { } locationId && await uow.Locations.GetByIdAsync(locationId, cancellationToken) is null)
        {
            validation.Add(nameof(TrackedPartSettingsInput.DefaultLocationId), StockMessages.LocationNotFound);
        }

        if (!validation.IsValid)
        {
            return Result<TrackedPartDetails>.Failure(validation.Errors);
        }

        var tracked = await uow.TrackedParts.GetByPartIdAsync(partId, cancellationToken);
        if (tracked is null)
        {
            return Error.NotFound(StockMessages.PartNotTracked);
        }

        uow.TrackedParts.SetExpectedVersion(tracked, input.Version);
        if (await parts.GetAsync(partId, cancellationToken) is { } summary)
        {
            tracked.Refresh(summary);
        }

        tracked.MinimumThreshold = input.MinimumThreshold;
        tracked.DefaultLocationId = input.DefaultLocationId;
        tracked.Notes = Inputs.Clean(input.Notes);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? await ReloadAsync(partId, cancellationToken) : Result<TrackedPartDetails>.Failure(saved.Errors);
    }

    public async Task<Result<TrackedPartDetails>> SaveSourceAsync(Guid partId, Guid? sourceId, BlankSourceInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<TrackedPartDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var tracked = await TrackedPartRegistry.EnsureTrackedAsync(uow, parts, partId, cancellationToken);
        if (tracked.IsFailure)
        {
            return Result<TrackedPartDetails>.Failure(tracked.Errors);
        }

        var part = tracked.Value;
        var source = sourceId is { } id ? part.Sources.FirstOrDefault(s => s.Id == id) : null;
        if (sourceId is not null && source is null)
        {
            return Error.NotFound(StockMessages.SourceNotFound);
        }

        var validation = new Validation()
            .When(input.QuantityPerPart <= 0, nameof(BlankSourceInput.QuantityPerPart), StockMessages.QuantityMustBePositive)
            .When(Math.Round(input.QuantityPerPart, QuantityPerPartDecimals) != input.QuantityPerPart, nameof(BlankSourceInput.QuantityPerPart), StockMessages.QuantityPerPartPrecision)
            .MaxLength(input.Note, BlankSource.NoteMaxLength, nameof(BlankSourceInput.Note), StockMessages.TextTooLong);
        if (input.RevisionId is { } revisionId && (await routings.GetRevisionsAsync(partId, cancellationToken)).All(r => r.Id != revisionId))
        {
            validation.Add(nameof(BlankSourceInput.RevisionId), StockMessages.RevisionNotFound);
        }

        var afterOperation = input.SourceAfterOperation;
        if (input.Kind == BlankSourceKind.Item)
        {
            var item = input.ItemId is { } itemId ? await uow.Items.GetByIdAsync(itemId, cancellationToken) : null;
            if (item is null || (item.IsArchived && source?.ItemId != item.Id))
            {
                validation.Add(nameof(BlankSourceInput.ItemId), StockMessages.ItemNotFound);
            }
        }
        else if (input.SourcePartId is not { } sourcePartId || await parts.GetAsync(sourcePartId, cancellationToken) is null)
        {
            validation.Add(nameof(BlankSourceInput.SourcePartId), StockMessages.PartNotFound);
        }
        else
        {
            var edges = (await uow.TrackedParts.ListSourcesAsync(cancellationToken))
                .Where(e => e.Source.Kind == BlankSourceKind.Part && e.Source.SourcePartId is not null)
                .GroupBy(e => e.PartId)
                .ToDictionary(g => g.Key, g => (IReadOnlyCollection<Guid>)g.Select(e => e.Source.SourcePartId!.Value).ToList());
            if (BlankSourceGraph.WouldCreateCycle(partId, sourcePartId, edges))
            {
                validation.Add(nameof(BlankSourceInput.SourcePartId), StockMessages.SourceCycle);
            }

            if (afterOperation is { } number)
            {
                // Le numéro doit exister dans la gamme de la source ; s'il s'agit de sa dernière opération, la source est « finie ».
                var routing = await InForceRoutingAsync(sourcePartId, cancellationToken);
                if (routing is null || routing.Operations.All(o => o.Number != number))
                {
                    validation.Add(nameof(BlankSourceInput.SourceAfterOperation), StockMessages.SourceOperationUnknown);
                }
                else if (routing.Operations.Max(o => o.Number) == number)
                {
                    afterOperation = null;
                }
            }
        }

        var target = (input.Kind, input.Kind == BlankSourceKind.Item ? input.ItemId : input.SourcePartId, afterOperation, input.RevisionId);
        if (validation.IsValid && part.Sources.Any(s => s != source && (s.Kind, s.Kind == BlankSourceKind.Item ? s.ItemId : s.SourcePartId, s.SourceAfterOperation, s.RevisionId) == target))
        {
            validation.Add(null, StockMessages.SourceDuplicate);
        }

        if (!validation.IsValid)
        {
            return Result<TrackedPartDetails>.Failure(validation.Errors);
        }

        if (source is null)
        {
            source = part.AddSource(input.Kind, input.ItemId, input.SourcePartId, afterOperation, input.QuantityPerPart, input.RevisionId);
            uow.TrackedParts.AddSource(source);
        }
        else
        {
            source.Retarget(input.Kind, input.ItemId, input.SourcePartId, afterOperation);
            source.QuantityPerPart = input.QuantityPerPart;
            source.RevisionId = input.RevisionId;
        }

        source.Note = Inputs.Clean(input.Note);
        if (input.IsDefault || !part.Sources.Any(s => s != source && s.RevisionId == source.RevisionId && s.IsDefault))
        {
            part.MakeDefault(source);
        }
        else
        {
            source.IsDefault = false;
        }

        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? await ReloadAsync(partId, cancellationToken) : Result<TrackedPartDetails>.Failure(saved.Errors);
    }

    public async Task<Result<TrackedPartDetails>> RemoveSourceAsync(Guid partId, Guid sourceId, CancellationToken cancellationToken = default)
    {
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<TrackedPartDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var part = await uow.TrackedParts.GetByPartIdAsync(partId, cancellationToken);
        var source = part?.Sources.FirstOrDefault(s => s.Id == sourceId);
        if (part is null || source is null)
        {
            return Error.NotFound(StockMessages.SourceNotFound);
        }

        part.RemoveSource(source);
        uow.TrackedParts.RemoveSource(source);

        // La portée garde une source par défaut s'il en reste.
        if (source.IsDefault && part.Sources.Where(s => s.RevisionId == source.RevisionId).OrderBy(s => s.SortOrder).FirstOrDefault() is { } next)
        {
            part.MakeDefault(next);
        }

        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? await ReloadAsync(partId, cancellationToken) : Result<TrackedPartDetails>.Failure(saved.Errors);
    }

    public Task<Result<IReadOnlyList<SourceUsage>>> ListPartsMadeFromItemAsync(Guid itemId, CancellationToken cancellationToken = default) =>
        ListUsagesAsync(s => s.Kind == BlankSourceKind.Item && s.ItemId == itemId, cancellationToken);

    public Task<Result<IReadOnlyList<SourceUsage>>> ListPartsMadeFromPartAsync(Guid partId, CancellationToken cancellationToken = default) =>
        ListUsagesAsync(s => s.Kind == BlankSourceKind.Part && s.SourcePartId == partId, cancellationToken);

    private async Task<Result<IReadOnlyList<SourceUsage>>> ListUsagesAsync(Func<BlankSource, bool> predicate, CancellationToken cancellationToken)
    {
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<IReadOnlyList<SourceUsage>>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var usages = (await uow.TrackedParts.ListSourcesAsync(cancellationToken)).Where(e => predicate(e.Source)).ToList();
        if (usages.Count == 0)
        {
            return Result.Success<IReadOnlyList<SourceUsage>>([]);
        }

        var tracked = (await uow.TrackedParts.GetManyAsync(usages.Select(u => u.PartId), cancellationToken)).ToDictionary(t => t.PartId);
        var summaries = await parts.GetManyAsync(usages.Select(u => u.PartId), cancellationToken);
        var availability = new SourceAvailability(uow);
        var result = new List<SourceUsage>();
        foreach (var (partId, source) in usages)
        {
            summaries.TryGetValue(partId, out var summary);
            var revisionIndex = source.RevisionId is { } revisionId
                ? (await routings.GetRevisionsAsync(partId, cancellationToken)).FirstOrDefault(r => r.Id == revisionId)?.Index
                : null;
            var available = await availability.AvailableAsync(source, cancellationToken);
            result.Add(new SourceUsage(
                partId, summary?.InternalReference ?? tracked[partId].LastKnownReference, summary?.Designation ?? tracked[partId].LastKnownDesignation,
                source.QuantityPerPart, source.SourceAfterOperation, revisionIndex, SourceAvailability.Launchable(available, source.QuantityPerPart)));
        }

        return Result.Success<IReadOnlyList<SourceUsage>>(result.OrderBy(u => u.Reference, StringComparer.CurrentCultureIgnoreCase).ToList());
    }

    private async Task<Result<TrackedPartDetails>> ReloadAsync(Guid partId, CancellationToken cancellationToken)
    {
        await using var uow = unitOfWorkFactory.Create();
        return await DetailsAsync(uow, partId, cancellationToken);
    }

    private async Task<Result<TrackedPartDetails>> DetailsAsync(IStockUnitOfWork uow, Guid partId, CancellationToken cancellationToken)
    {
        var summary = await parts.GetAsync(partId, cancellationToken);
        var tracked = (await uow.TrackedParts.GetManyAsync([partId], cancellationToken)).SingleOrDefault();
        if (summary is null && tracked is null)
        {
            return Error.NotFound(StockMessages.PartNotFound);
        }

        var revisions = summary is null ? [] : await routings.GetRevisionsAsync(partId, cancellationToken);
        var inForce = revisions.FirstOrDefault(r => r.Status == RevisionStatus.InForce);
        var customer = summary is null ? null : await customers.GetAsync(summary.CustomerId, cancellationToken);
        var balances = await uow.Balances.ListAsync(new BalanceFilter { SubjectKind = StockSubjectKind.Part, PartId = partId }, cancellationToken);
        var names = await StockNames.LoadAsync(uow, parts, balances.Select(b => b.Key), cancellationToken);
        var finishedStock = balances.Where(b => b.Bucket == StockBucket.Stock).Select(names.Cell)
            .OrderBy(c => c.LocationCode, StringComparer.CurrentCultureIgnoreCase).ThenBy(c => c.Key.Lot, StringComparer.Ordinal).ToList();

        var sources = new List<BlankSourceView>();
        if (tracked is not null)
        {
            var availability = new SourceAvailability(uow);
            var items = (await uow.Items.GetManyAsync(tracked.Sources.Where(s => s.ItemId is not null).Select(s => s.ItemId!.Value), cancellationToken)).ToDictionary(i => i.Id);
            var sourceParts = await parts.GetManyAsync(tracked.Sources.Where(s => s.SourcePartId is not null).Select(s => s.SourcePartId!.Value), cancellationToken);
            foreach (var source in tracked.Sources.OrderBy(s => s.RevisionId is null ? 0 : 1).ThenByDescending(s => s.IsDefault).ThenBy(s => s.SortOrder))
            {
                var available = await availability.AvailableAsync(source, cancellationToken);
                var (code, label, unit) = source.Kind == BlankSourceKind.Item
                    ? items.TryGetValue(source.ItemId!.Value, out var item) ? (item.Code, item.Designation, item.Unit) : ("?", StockMessages.UnknownItem, StockUnit.Piece)
                    : sourceParts.TryGetValue(source.SourcePartId!.Value, out var sourcePart) ? (sourcePart.InternalReference, sourcePart.Designation, StockUnit.Piece) : ("?", StockMessages.UnknownPart, StockUnit.Piece);
                sources.Add(new BlankSourceView(
                    source.Id, source.Kind, source.ItemId, source.SourcePartId, code, label, unit, source.SourceAfterOperation, source.QuantityPerPart,
                    source.RevisionId, revisions.FirstOrDefault(r => r.Id == source.RevisionId)?.Index, source.IsDefault, source.SortOrder, source.Note,
                    available, SourceAvailability.Launchable(available, source.QuantityPerPart)));
            }
        }

        var hint = await HintAsync(uow, inForce, cancellationToken);
        return new TrackedPartDetails(
            partId, summary?.InternalReference ?? tracked!.LastKnownReference, summary?.Designation ?? tracked!.LastKnownDesignation,
            summary?.CustomerId, customer?.Display, summary is null, summary?.Status, summary?.IsArchived ?? false, tracked is not null,
            inForce, revisions, tracked?.MinimumThreshold, tracked?.DefaultLocationId,
            tracked?.DefaultLocationId is { } locationId ? names.Location(locationId)?.Code : null, tracked?.Notes, tracked?.Version ?? 0,
            (int)finishedStock.Sum(c => c.Quantity),
            (int)balances.Where(b => b.Bucket == StockBucket.WorkInProgress).Sum(b => b.Quantity),
            finishedStock, sources, hint);
    }

    /// <summary>Matière, forme et dimensions du brut de l'indice en vigueur, et l'article proposé pour cette matière.</summary>
    private async Task<BlankHint> HintAsync(IStockUnitOfWork uow, PartRevisionSummary? revision, CancellationToken cancellationToken)
    {
        if (revision is null)
        {
            return new BlankHint(null, null, null, null, null, null);
        }

        string? materialName = null;
        StockItem? suggested = null;
        if (revision.MaterialId is { } materialId)
        {
            materialName = (await materials.GetManyAsync([materialId], cancellationToken)).GetValueOrDefault(materialId)?.Display;
            suggested = (await uow.Items.ListAsync(includeArchived: false, cancellationToken))
                .Where(i => i.MaterialId == materialId)
                .OrderBy(i => i.Shape == revision.BlankShape ? 0 : 1)
                .ThenBy(i => i.Code, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        return new BlankHint(revision.MaterialId, materialName, revision.BlankShape, revision.BlankDimensions, suggested?.Id,
            suggested is null ? null : $"{suggested.Code} — {suggested.Designation}");
    }

    private async Task<RevisionRouting?> InForceRoutingAsync(Guid partId, CancellationToken cancellationToken)
    {
        var inForce = (await routings.GetRevisionsAsync(partId, cancellationToken)).FirstOrDefault(r => r.Status == RevisionStatus.InForce);
        return inForce is null ? null : await routings.GetRoutingAsync(inForce.Id, cancellationToken);
    }

    /// <summary>Vrai si des pièces finies sont à un indice qui n'est plus en vigueur.</summary>
    private async Task<bool> HasSupersededStockAsync(Guid partId, IReadOnlyCollection<StockBalance> balances, CancellationToken cancellationToken)
    {
        var finishedRevisions = balances.Where(b => b.Bucket == StockBucket.Stock).Select(b => b.RevisionId).Distinct().ToList();
        if (finishedRevisions.Count == 0)
        {
            return false;
        }

        var revisions = await routings.GetRevisionsAsync(partId, cancellationToken);
        return finishedRevisions.Any(id => revisions.FirstOrDefault(r => r.Id == id)?.Status != RevisionStatus.InForce);
    }
}

/// <summary>Création à la demande d'une pièce suivie (explicite, ou au premier mouvement de la pièce).</summary>
internal static class TrackedPartRegistry
{
    public static async Task<Result<TrackedPart>> EnsureTrackedAsync(IStockUnitOfWork uow, IPartLookup parts, Guid partId, CancellationToken cancellationToken)
    {
        var tracked = await uow.TrackedParts.GetByPartIdAsync(partId, cancellationToken);
        var summary = await parts.GetAsync(partId, cancellationToken);
        if (tracked is not null)
        {
            if (summary is not null)
            {
                tracked.Refresh(summary);
            }

            return tracked;
        }

        if (summary is null)
        {
            return Error.NotFound(StockMessages.PartNotFound);
        }

        tracked = new TrackedPart(summary);
        uow.TrackedParts.Add(tracked);
        return tracked;
    }
}

/// <summary>Quantité disponible d'une source de brut et nombre de pièces qu'elle permet de lancer.</summary>
internal sealed class SourceAvailability(IStockUnitOfWork uow)
{
    public async Task<decimal> AvailableAsync(BlankSource source, CancellationToken cancellationToken)
    {
        if (source.Kind == BlankSourceKind.Item)
        {
            var balances = await uow.Balances.ListAsync(new BalanceFilter { SubjectKind = StockSubjectKind.Item, ItemId = source.ItemId }, cancellationToken);
            return balances.Sum(b => b.Quantity);
        }

        var partBalances = await uow.Balances.ListAsync(new BalanceFilter { SubjectKind = StockSubjectKind.Part, PartId = source.SourcePartId }, cancellationToken);
        if (source.SourceAfterOperation is not { } operation)
        {
            return partBalances.Where(b => b.Bucket == StockBucket.Stock).Sum(b => b.Quantity);
        }

        // Pièces en cours « après l'opération n° » : étape lue dans la gamme figée de chaque lancement en cours de la source.
        var wip = partBalances.Where(b => b.Bucket == StockBucket.WorkInProgress).ToList();
        var batches = (await uow.Batches.GetManyAsync(wip.Select(b => b.BatchId), cancellationToken)).ToDictionary(b => b.Id);
        return wip.Where(b => batches.TryGetValue(b.BatchId, out var batch) && batch.Status == Stock.Contracts.BatchStatus.InProgress
            && batch.StageAfterOperation(operation) == b.Stage).Sum(b => b.Quantity);
    }

    public static int Launchable(decimal available, decimal quantityPerPart) =>
        quantityPerPart <= 0 || available <= 0 ? 0 : (int)Math.Floor(available / quantityPerPart);
}
