using System.Globalization;
using Barthazon.Catalog.Contracts;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Persistence;
using Barthazon.Stock.Core.Resources;

namespace Barthazon.Stock.Core.Services;

/// <summary>Inventaires : ouverture, comptage, écarts, validation par des corrections (section 10.9).</summary>
public interface IInventoryService
{
    Task<Result<IReadOnlyList<InventoryListItem>>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Inventaire par son numéro (« INV2026-03 »).</summary>
    Task<Result<InventoryDetails>> GetByNumberAsync(string number, CancellationToken cancellationToken = default);

    /// <summary>Ouvre un inventaire : une ligne par case de stock de la portée, quantité attendue = solde actuel (éditeur).</summary>
    Task<Result<InventoryDetails>> OpenAsync(OpenInventoryInput input, CancellationToken cancellationToken = default);

    /// <summary>Enregistre des quantités comptées (vide = pas encore compté).</summary>
    Task<Result<InventoryDetails>> CountAsync(Guid inventoryId, IReadOnlyList<InventoryCountInput> counts, CancellationToken cancellationToken = default);

    /// <summary>Ajoute une ligne imprévue : objet trouvé là où il n'était pas attendu.</summary>
    Task<Result<InventoryDetails>> AddLineAsync(Guid inventoryId, InventoryLineInput input, CancellationToken cancellationToken = default);

    /// <summary>Retire une ligne imprévue ajoutée par erreur.</summary>
    Task<Result<InventoryDetails>> RemoveLineAsync(Guid inventoryId, Guid lineId, CancellationToken cancellationToken = default);

    /// <summary>Valide : une correction (motif « Inventaire ») par case comptée en écart ; les cases non comptées ne changent pas.</summary>
    Task<Result<InventoryDetails>> ValidateAsync(Guid inventoryId, CancellationToken cancellationToken = default);

    /// <summary>Annule un inventaire ouvert : le stock ne change pas.</summary>
    Task<Result<InventoryDetails>> CancelAsync(Guid inventoryId, CancellationToken cancellationToken = default);
}

internal sealed class InventoryService(
    IUnitOfWorkFactory<IStockUnitOfWork> unitOfWorkFactory,
    StockLedger ledger,
    MovementService movements,
    StockAccess access,
    TimeProvider timeProvider,
    IPartLookup parts,
    IPartRoutingLookup routings) : IInventoryService
{
    public async Task<Result<IReadOnlyList<InventoryListItem>>> ListAsync(CancellationToken cancellationToken = default)
    {
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<IReadOnlyList<InventoryListItem>>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var locations = (await uow.Locations.ListAsync(includeArchived: true, cancellationToken)).ToDictionary(l => l.Id);
        var inventories = await uow.Inventories.ListAsync(cancellationToken);
        return Result.Success<IReadOnlyList<InventoryListItem>>(inventories.Select(i => new InventoryListItem(
            i.Id, i.Number, i.Title, i.Status, i.StartedAt, i.ClosedAt, i.ClosedByName, i.CreatedBy,
            ScopeLabel(i, locations), i.IncludeWorkInProgress, i.Lines.Count,
            i.Lines.Count(l => l.CountedQuantity is not null), i.Lines.Count(l => l.Difference is { } d && d != 0))).ToList());
    }

    public async Task<Result<InventoryDetails>> GetByNumberAsync(string number, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(number);
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<InventoryDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var inventory = await uow.Inventories.GetByNumberAsync(number, cancellationToken);
        return inventory is null ? Error.NotFound(StockMessages.InventoryNotFound) : await DetailsAsync(uow, inventory, cancellationToken);
    }

    public async Task<Result<InventoryDetails>> OpenAsync(OpenInventoryInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<InventoryDetails>.Failure(allowed.Errors);
        }

        var now = timeProvider.GetUtcNow();
        var today = TimeZoneInfo.ConvertTime(now, timeProvider.LocalTimeZone);
        var title = string.IsNullOrWhiteSpace(input.Title)
            ? string.Format(CultureInfo.CurrentCulture, StockMessages.InventoryDefaultTitle, today.ToString("d", CultureInfo.CurrentCulture))
            : input.Title.Trim();
        var validation = new Validation().MaxLength(title, InventoryCount.TitleMaxLength, nameof(OpenInventoryInput.Title), StockMessages.TextTooLong);
        for (var attempt = 0; ; attempt++)
        {
            await using var uow = unitOfWorkFactory.Create();
            var locations = (await uow.Locations.ListAsync(includeArchived: false, cancellationToken)).ToDictionary(l => l.Id);
            var scope = input.LocationIds.Distinct().ToList();
            if (scope.Any(id => !locations.ContainsKey(id)))
            {
                validation.Add(nameof(OpenInventoryInput.LocationIds), StockMessages.LocationNotFound);
            }

            if (!validation.IsValid)
            {
                return Result<InventoryDetails>.Failure(validation.Errors);
            }

            var sequence = await uow.Inventories.GetLastYearSequenceAsync(today.Year, cancellationToken) + 1;
            var number = string.Format(CultureInfo.InvariantCulture, "INV{0}-{1:00}", today.Year, sequence);
            var inventory = new InventoryCount(number, today.Year, sequence, title, scope, input.IncludeWorkInProgress, now);
            var balances = await uow.Balances.ListAsync(new BalanceFilter(), cancellationToken);
            foreach (var balance in balances.Where(b => inventory.Covers(b.LocationId) && (input.IncludeWorkInProgress || b.Bucket == StockBucket.Stock)))
            {
                inventory.AddLine(balance.Key, balance.Quantity, unexpected: false);
            }

            uow.Inventories.Add(inventory);
            var saved = await uow.SaveChangesAsync(cancellationToken);
            if (saved.IsSuccess)
            {
                return await GetByNumberAsync(number, cancellationToken);
            }

            // Deux ouvertures simultanées : la seconde est renumérotée (nouvel essai).
            if (!saved.HasError(ErrorKind.Conflict) || attempt > 0)
            {
                return Result<InventoryDetails>.Failure(saved.Errors);
            }
        }
    }

    public async Task<Result<InventoryDetails>> CountAsync(Guid inventoryId, IReadOnlyList<InventoryCountInput> counts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(counts);
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<InventoryDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var inventory = await OpenInventoryAsync(uow, inventoryId, cancellationToken);
        if (inventory.IsFailure)
        {
            return Result<InventoryDetails>.Failure(inventory.Errors);
        }

        var lines = inventory.Value.Lines.ToDictionary(l => l.Id);
        var items = await ItemsAsync(uow, inventory.Value.Lines.Select(l => l.Key), cancellationToken);
        var validation = new Validation();
        var now = timeProvider.GetUtcNow();
        var by = access.User.DisplayName ?? access.User.UserName;
        for (var i = 0; i < counts.Count; i++)
        {
            var count = counts[i];
            if (!lines.TryGetValue(count.LineId, out var line))
            {
                validation.Add($"Lines[{i}]", StockMessages.InventoryLineNotFound);
                continue;
            }

            if (count.Counted is { } counted && CheckQuantity(counted, line.Key, items) is { } error)
            {
                validation.Add($"Lines[{i}]", error);
                continue;
            }

            if (line.CountedQuantity != count.Counted)
            {
                line.Count(count.Counted, now, by);
            }
        }

        if (!validation.IsValid)
        {
            return Result<InventoryDetails>.Failure(validation.Errors);
        }

        return await SaveAndReloadAsync(uow, inventory.Value, cancellationToken);
    }

    public async Task<Result<InventoryDetails>> AddLineAsync(Guid inventoryId, InventoryLineInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<InventoryDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var inventory = await OpenInventoryAsync(uow, inventoryId, cancellationToken);
        if (inventory.IsFailure)
        {
            return Result<InventoryDetails>.Failure(inventory.Errors);
        }

        var validation = new Validation()
            .MaxLength(input.Lot, StockKey.LotMaxLength, nameof(InventoryLineInput.Lot), StockMessages.TextTooLong);
        var location = await uow.Locations.GetByIdAsync(input.LocationId, cancellationToken);
        if (location is null || location.IsArchived)
        {
            validation.Add(nameof(InventoryLineInput.LocationId), StockMessages.LocationNotFound);
        }
        else if (!inventory.Value.Covers(location.Id))
        {
            validation.Add(nameof(InventoryLineInput.LocationId), StockMessages.InventoryLocationOutOfScope);
        }

        StockKey? key = null;
        if (input.ItemId is { } itemId)
        {
            var item = await uow.Items.GetByIdAsync(itemId, cancellationToken);
            if (item is null)
            {
                validation.Add(nameof(InventoryLineInput.ItemId), StockMessages.ItemNotFound);
            }
            else
            {
                key = StockKey.ForItem(item.Id, input.LocationId, input.Lot);
            }
        }
        else if (input.PartId is { } partId)
        {
            var revisions = await routings.GetRevisionsAsync(partId, cancellationToken);
            var revision = input.RevisionId is { } revisionId
                ? revisions.FirstOrDefault(r => r.Id == revisionId)
                : revisions.FirstOrDefault(r => r.Status == RevisionStatus.InForce);
            if (revision is null)
            {
                validation.Add(nameof(InventoryLineInput.RevisionId), StockMessages.RevisionRequired);
            }
            else
            {
                key = StockKey.ForFinishedPart(partId, revision.Id, input.LocationId, input.Lot);
            }
        }
        else
        {
            validation.Add(nameof(InventoryLineInput.ItemId), StockMessages.InventorySubjectRequired);
        }

        if (key is { } k)
        {
            if (inventory.Value.Lines.Any(l => l.Key == k))
            {
                validation.Add(nameof(InventoryLineInput.ItemId), StockMessages.InventoryLineExists);
            }

            var items = await ItemsAsync(uow, [k], cancellationToken);
            if (CheckQuantity(input.Counted, k, items) is { } error)
            {
                validation.Add(nameof(InventoryLineInput.Counted), error);
            }
        }

        if (!validation.IsValid)
        {
            return Result<InventoryDetails>.Failure(validation.Errors);
        }

        var cell = key!.Value;
        if (!cell.IsItem)
        {
            var tracked = await TrackedPartRegistry.EnsureTrackedAsync(uow, parts, cell.PartId, cancellationToken);
            if (tracked.IsFailure)
            {
                return Result<InventoryDetails>.Failure(tracked.Errors);
            }
        }

        var expected = (await uow.Balances.GetForUpdateAsync([cell], cancellationToken)).SingleOrDefault()?.Quantity ?? 0m;
        var line = inventory.Value.AddLine(cell, expected, unexpected: true);
        line.Count(input.Counted, timeProvider.GetUtcNow(), access.User.DisplayName ?? access.User.UserName);
        return await SaveAndReloadAsync(uow, inventory.Value, cancellationToken);
    }

    public async Task<Result<InventoryDetails>> RemoveLineAsync(Guid inventoryId, Guid lineId, CancellationToken cancellationToken = default)
    {
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<InventoryDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var inventory = await OpenInventoryAsync(uow, inventoryId, cancellationToken);
        if (inventory.IsFailure)
        {
            return Result<InventoryDetails>.Failure(inventory.Errors);
        }

        var line = inventory.Value.Lines.FirstOrDefault(l => l.Id == lineId);
        if (line is null)
        {
            return Error.NotFound(StockMessages.InventoryLineNotFound);
        }

        if (!line.IsUnexpected)
        {
            return Error.Validation(StockMessages.InventoryLineNotRemovable);
        }

        inventory.Value.RemoveLine(line);
        return await SaveAndReloadAsync(uow, inventory.Value, cancellationToken);
    }

    public async Task<Result<InventoryDetails>> ValidateAsync(Guid inventoryId, CancellationToken cancellationToken = default)
    {
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<InventoryDetails>.Failure(allowed.Errors);
        }

        var by = access.User.DisplayName ?? access.User.UserName;
        await using (var check = unitOfWorkFactory.Create())
        {
            var inventory = await OpenInventoryAsync(check, inventoryId, cancellationToken);
            if (inventory.IsFailure)
            {
                return Result<InventoryDetails>.Failure(inventory.Errors);
            }

            if (inventory.Value.Lines.All(l => l.CountedQuantity is null))
            {
                return Error.Validation(StockMessages.InventoryNothingCounted);
            }

            // Aucun écart : l'inventaire est validé sans mouvement.
            if (!inventory.Value.Lines.Any(l => l.Difference is { } d && d != 0))
            {
                inventory.Value.Validate(timeProvider.GetUtcNow(), by);
                return await SaveAndReloadAsync(check, inventory.Value, cancellationToken);
            }
        }

        string? number = null;
        var recorded = await ledger.RecordAsync(async uow =>
        {
            var inventory = await OpenInventoryAsync(uow, inventoryId, cancellationToken);
            if (inventory.IsFailure)
            {
                return Result<LedgerEntry>.Failure(inventory.Errors);
            }

            number = inventory.Value.Number;
            var now = timeProvider.GetUtcNow();
            var entry = new LedgerEntry();
            foreach (var line in inventory.Value.Lines.Where(l => l.Difference is { } d && d != 0))
            {
                var transaction = entry.Add(movements.NewTransaction(StockTransactionType.Adjustment, now));
                transaction.Reason = StockReason.Inventory;
                transaction.Reference = inventory.Value.Number;
                transaction.Comment = inventory.Value.Title.Length <= StockTransaction.CommentMaxLength ? inventory.Value.Title : null;
                transaction.BatchId = line.Key.IsWorkInProgress ? line.BatchId : null;
                transaction.AddLine(line.Key, line.Difference!.Value);
            }

            var validated = inventory.Value;
            entry.BeforeSave.Add(() => validated.Validate(now, by));
            return entry;
        }, cancellationToken);

        return recorded.IsFailure ? Result<InventoryDetails>.Failure(recorded.Errors) : await GetByNumberAsync(number!, cancellationToken);
    }

    public async Task<Result<InventoryDetails>> CancelAsync(Guid inventoryId, CancellationToken cancellationToken = default)
    {
        var allowed = access.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<InventoryDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var inventory = await OpenInventoryAsync(uow, inventoryId, cancellationToken);
        if (inventory.IsFailure)
        {
            return Result<InventoryDetails>.Failure(inventory.Errors);
        }

        inventory.Value.Cancel(timeProvider.GetUtcNow(), access.User.DisplayName ?? access.User.UserName);
        return await SaveAndReloadAsync(uow, inventory.Value, cancellationToken);
    }

    private static async Task<Result<InventoryCount>> OpenInventoryAsync(IStockUnitOfWork uow, Guid inventoryId, CancellationToken cancellationToken)
    {
        var inventory = await uow.Inventories.GetWithLinesAsync(inventoryId, cancellationToken);
        if (inventory is null)
        {
            return Error.NotFound(StockMessages.InventoryNotFound);
        }

        return inventory.IsOpen ? inventory : Error.Validation(StockMessages.InventoryClosed);
    }

    private async Task<Result<InventoryDetails>> SaveAndReloadAsync(IStockUnitOfWork uow, InventoryCount inventory, CancellationToken cancellationToken)
    {
        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsFailure ? Result<InventoryDetails>.Failure(saved.Errors) : await GetByNumberAsync(inventory.Number, cancellationToken);
    }

    private static async Task<Dictionary<Guid, StockItem>> ItemsAsync(IStockUnitOfWork uow, IEnumerable<StockKey> keys, CancellationToken cancellationToken)
    {
        var ids = keys.Where(k => k.IsItem).Select(k => k.ItemId).Distinct().ToList();
        return ids.Count == 0 ? [] : (await uow.Items.GetManyAsync(ids, cancellationToken)).ToDictionary(i => i.Id);
    }

    /// <summary>Quantité comptée : positive, entière pour les pièces, trois décimales au plus pour les autres unités.</summary>
    private static string? CheckQuantity(decimal counted, StockKey key, Dictionary<Guid, StockItem> items)
    {
        if (counted < 0)
        {
            return StockMessages.MustBePositive;
        }

        var unit = key.IsItem && items.TryGetValue(key.ItemId, out var item) ? item.Unit : StockUnit.Piece;
        return StockFormat.FitsUnit(counted, unit) ? null : StockMessages.QuantityPrecision;
    }

    private async Task<Result<InventoryDetails>> DetailsAsync(IStockUnitOfWork uow, InventoryCount inventory, CancellationToken cancellationToken)
    {
        var keys = inventory.Lines.Select(l => l.Key).ToList();
        var names = await StockNames.LoadAsync(uow, parts, keys, cancellationToken);
        var moved = inventory.IsOpen
            ? (await uow.Transactions.ListKeysRecordedAfterAsync(inventory.StartedAt, cancellationToken)).ToHashSet()
            : [];
        var revisionIndexes = new Dictionary<Guid, string>();
        foreach (var partId in keys.Where(k => !k.IsItem).Select(k => k.PartId).Distinct())
        {
            foreach (var revision in await routings.GetRevisionsAsync(partId, cancellationToken))
            {
                revisionIndexes[revision.Id] = revision.Index;
            }
        }

        var locations = (await uow.Locations.ListAsync(includeArchived: true, cancellationToken)).ToDictionary(l => l.Id);
        var scope = inventory.LocationIds.Count == 0
            ? locations.Values.Where(l => !l.IsArchived)
            : inventory.LocationIds.Where(locations.ContainsKey).Select(id => locations[id]);
        var groups = scope
            .Concat(inventory.Lines.Select(l => l.LocationId).Distinct().Where(locations.ContainsKey).Select(id => locations[id]))
            .DistinctBy(l => l.Id)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Code, StringComparer.CurrentCultureIgnoreCase)
            .Select(location => new InventoryLocationGroup(location.Id, location.Code, location.Name, inventory.Lines
                .Where(l => l.LocationId == location.Id)
                .Select(l => new InventoryLineView(
                    l.Id, l.Key, names.SubjectCode(l.Key), names.SubjectLabel(l.Key),
                    l.Key.IsItem ? null : revisionIndexes.GetValueOrDefault(l.RevisionId, "?"),
                    names.StageLabel(l.Key), names.Unit(l.Key), l.ExpectedQuantity, l.CountedQuantity, l.CountedAt, l.CountedByName,
                    l.IsUnexpected, moved.Contains(l.Key)))
                .OrderBy(l => l.Key.Bucket).ThenBy(l => l.Key.SubjectKind).ThenBy(l => l.SubjectCode, StringComparer.CurrentCultureIgnoreCase)
                .ThenBy(l => l.Key.Lot, StringComparer.Ordinal).ThenBy(l => l.Key.Stage)
                .ToList()))
            .ToList();
        return new InventoryDetails(
            inventory.Id, inventory.Number, inventory.Title, inventory.Status, inventory.StartedAt, inventory.ClosedAt, inventory.ClosedByName,
            inventory.CreatedBy, ScopeLabel(inventory, locations), inventory.IncludeWorkInProgress, groups);
    }

    private static string ScopeLabel(InventoryCount inventory, Dictionary<Guid, StockLocation> locations) =>
        inventory.LocationIds.Count == 0
            ? StockMessages.InventoryWholeStock
            : string.Join(", ", inventory.LocationIds.Select(id => locations.TryGetValue(id, out var l) ? l.Code : "?"));
}
