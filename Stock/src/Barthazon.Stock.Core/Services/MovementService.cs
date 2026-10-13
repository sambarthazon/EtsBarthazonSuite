using System.Globalization;
using Barthazon.Catalog.Contracts;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Persistence;
using Barthazon.Stock.Core.Resources;

namespace Barthazon.Stock.Core.Services;

/// <summary>Mouvements hors lancement (réception, sortie, transfert, correction), journal et annulation.</summary>
public interface IMovementService
{
    /// <summary>Réception d'un article (connecté).</summary>
    Task<Result<MovementItem>> ReceiveItemAsync(ItemMovementInput input, CancellationToken cancellationToken = default);

    /// <summary>Sortie d'un article hors lancement : essai, retour fournisseur… (connecté).</summary>
    Task<Result<MovementItem>> IssueItemAsync(ItemMovementInput input, CancellationToken cancellationToken = default);

    /// <summary>Transfert d'une case vers un autre emplacement (connecté).</summary>
    Task<Result<MovementItem>> TransferAsync(TransferInput input, CancellationToken cancellationToken = default);

    /// <summary>Correction d'une case : la quantité constatée remplace la quantité connue (éditeur).</summary>
    Task<Result<MovementItem>> AdjustAsync(AdjustmentInput input, CancellationToken cancellationToken = default);

    /// <summary>Annulation : contre-mouvement aux lignes opposées (éditeur, ou l'auteur dans le délai).</summary>
    Task<Result<MovementItem>> ReverseAsync(Guid transactionId, string? comment = null, CancellationToken cancellationToken = default);

    Task<Result<PagedResult<MovementItem>>> SearchAsync(MovementSearchRequest request, CancellationToken cancellationToken = default);

    Task<Result<MovementDetails>> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>Mouvement par son numéro (« M-000123 » ou « 123 »).</summary>
    Task<Result<MovementDetails>> GetByNumberAsync(string number, CancellationToken cancellationToken = default);
}

internal sealed class MovementService(
    IUnitOfWorkFactory<IStockUnitOfWork> unitOfWorkFactory,
    StockLedger ledger,
    StockAccess access,
    StockOptions options,
    TimeProvider timeProvider,
    IPartLookup parts) : IMovementService
{
    public Task<Result<MovementItem>> ReceiveItemAsync(ItemMovementInput input, CancellationToken cancellationToken = default) =>
        RecordItemMovementAsync(input, StockTransactionType.ItemReceipt, cancellationToken);

    public Task<Result<MovementItem>> IssueItemAsync(ItemMovementInput input, CancellationToken cancellationToken = default) =>
        RecordItemMovementAsync(input, StockTransactionType.ItemIssue, cancellationToken);

    public async Task<Result<MovementItem>> TransferAsync(TransferInput input, CancellationToken cancellationToken = default)
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
                .When(input.Quantity <= 0, nameof(TransferInput.Quantity), StockMessages.QuantityMustBePositive)
                .When(input.ToLocationId == input.From.LocationId, nameof(TransferInput.ToLocationId), StockMessages.TransferSameLocation);
            var occurredAt = CheckDate(input.OccurredAt, validation);
            var destination = await uow.Locations.GetByIdAsync(input.ToLocationId, cancellationToken);
            if (destination is null || destination.IsArchived)
            {
                validation.Add(nameof(TransferInput.ToLocationId), StockMessages.LocationNotFound);
            }

            var unit = await UnitOfAsync(uow, input.From, cancellationToken);
            if (unit is null)
            {
                validation.Add(nameof(TransferInput.From), StockMessages.ItemNotFound);
            }
            else if (!StockFormat.FitsUnit(input.Quantity, unit.Value))
            {
                validation.Add(nameof(TransferInput.Quantity), StockMessages.QuantityPrecision);
            }

            if (!validation.IsValid)
            {
                return Result<LedgerEntry>.Failure(validation.Errors);
            }

            var entry = new LedgerEntry();
            var transaction = entry.Add(NewTransaction(StockTransactionType.Transfer, occurredAt));
            transaction.BatchId = input.From.IsWorkInProgress ? input.From.BatchId : null;
            transaction.Comment = Inputs.Clean(input.Comment);
            transaction.AddLine(input.From, -input.Quantity);
            transaction.AddLine(input.From.At(input.ToLocationId), input.Quantity);
            return entry;
        }, cancellationToken);
        return await ToItemAsync(recorded, cancellationToken);
    }

    public async Task<Result<MovementItem>> AdjustAsync(AdjustmentInput input, CancellationToken cancellationToken = default)
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
                .When(input.CountedQuantity < 0, nameof(AdjustmentInput.CountedQuantity), StockMessages.MustBePositive)
                .MaxLength(input.Comment, StockTransaction.CommentMaxLength, nameof(AdjustmentInput.Comment), StockMessages.TextTooLong)
                .When(input.Cell.Lot.Length > StockKey.LotMaxLength, nameof(AdjustmentInput.Cell), StockMessages.TextTooLong);
            var occurredAt = CheckDate(input.OccurredAt, validation);
            var location = await uow.Locations.GetByIdAsync(input.Cell.LocationId, cancellationToken);
            if (location is null)
            {
                validation.Add(nameof(AdjustmentInput.Cell), StockMessages.LocationNotFound);
            }

            var unit = await UnitOfAsync(uow, input.Cell, cancellationToken);
            if (unit is null)
            {
                validation.Add(nameof(AdjustmentInput.Cell), StockMessages.ItemNotFound);
            }
            else if (!StockFormat.FitsUnit(input.CountedQuantity, unit.Value))
            {
                validation.Add(nameof(AdjustmentInput.CountedQuantity), StockMessages.QuantityPrecision);
            }

            if (!validation.IsValid)
            {
                return Result<LedgerEntry>.Failure(validation.Errors);
            }

            var current = (await uow.Balances.GetForUpdateAsync([input.Cell], cancellationToken)).SingleOrDefault()?.Quantity ?? 0m;
            var delta = input.CountedQuantity - current;
            if (delta == 0)
            {
                return Error.Validation(StockMessages.AdjustmentNoDifference, nameof(AdjustmentInput.CountedQuantity));
            }

            var entry = new LedgerEntry();
            var transaction = entry.Add(NewTransaction(StockTransactionType.Adjustment, occurredAt));
            transaction.Reason = input.Reason;
            transaction.Comment = Inputs.Clean(input.Comment);
            transaction.BatchId = input.Cell.IsWorkInProgress ? input.Cell.BatchId : null;
            transaction.AddLine(input.Cell, delta);
            return entry;
        }, cancellationToken);
        return await ToItemAsync(recorded, cancellationToken);
    }

    public async Task<Result<MovementItem>> ReverseAsync(Guid transactionId, string? comment = null, CancellationToken cancellationToken = default)
    {
        var allowed = access.CanDeclare();
        if (allowed.IsFailure)
        {
            return Result<MovementItem>.Failure(allowed.Errors);
        }

        var recorded = await ledger.RecordAsync(async uow =>
        {
            var original = await uow.Transactions.GetAsync(transactionId, cancellationToken);
            if (original is null)
            {
                return Error.NotFound(StockMessages.MovementNotFound);
            }

            var refusal = ReversalRefusal(original);
            if (refusal is not null)
            {
                return Result<LedgerEntry>.Failure(refusal);
            }

            if (comment is { Length: > StockTransaction.CommentMaxLength })
            {
                return Error.Validation(StockMessages.TextTooLong, nameof(comment));
            }

            // Annulation refusée si une case deviendrait négative : on désigne le mouvement à annuler d'abord.
            var keys = original.Lines.Where(l => l.Delta > 0).Select(l => l.Key).Distinct().ToList();
            var balances = (await uow.Balances.GetForUpdateAsync(keys, cancellationToken)).ToDictionary(b => b.Key);
            foreach (var group in original.Lines.Where(l => l.Delta > 0).GroupBy(l => l.Key))
            {
                var available = balances.GetValueOrDefault(group.Key)?.Quantity ?? 0m;
                if (available < group.Sum(l => l.Delta) && !(group.Key.IsItem && options.AllowNegativeItemStock))
                {
                    var blocker = await uow.Transactions.FindLatestConsumerAsync(group.Key, original.Sequence, cancellationToken);
                    return Error.Validation(blocker is null
                        ? StockMessages.ReversalWouldBeNegative
                        : string.Format(CultureInfo.CurrentCulture, StockMessages.ReversalBlockedBy, blocker.Number));
                }
            }

            var entry = new LedgerEntry();
            var reversal = entry.Add(NewTransaction(StockTransactionType.Reversal, timeProvider.GetUtcNow()));
            reversal.ReversesTransactionId = original.Id;
            reversal.BatchId = original.BatchId;
            reversal.StepSequence = original.StepSequence;
            reversal.Reference = original.Reference;
            reversal.Comment = Inputs.Clean(comment);
            foreach (var line in original.Lines)
            {
                reversal.AddLine(line.Key, -line.Delta);
            }

            entry.BeforeSave.Add(() => original.MarkReversedBy(reversal.Id));
            return entry;
        }, cancellationToken);
        return await ToItemAsync(recorded, cancellationToken);
    }

    public async Task<Result<PagedResult<MovementItem>>> SearchAsync(MovementSearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<PagedResult<MovementItem>>.Failure(allowed.Errors);
        }

        var zone = timeProvider.LocalTimeZone;
        var filter = new MovementFilter
        {
            From = request.From is { } from ? StartOfDay(from, zone) : null,
            To = request.To is { } to ? StartOfDay(to.AddDays(1), zone) : null,
            Types = request.Type is { } type ? [type] : null,
            ItemId = request.ItemId,
            PartId = request.PartId,
            BatchId = request.BatchId,
            LocationId = request.LocationId,
            UserName = Inputs.Clean(request.UserName),
            IncludeReversed = request.IncludeReversed,
        };
        var pageSize = Math.Clamp(request.PageSize, 1, 500);
        await using var uow = unitOfWorkFactory.Create();
        var page = await uow.Transactions.SearchAsync(filter, new PageRequest(Math.Max(request.PageIndex, 0), pageSize), cancellationToken);
        var items = await DescribeAsync(uow, page.Items, cancellationToken);
        return new PagedResult<MovementItem>(items, page.TotalCount);
    }

    public async Task<Result<MovementDetails>> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<MovementDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var transaction = await uow.Transactions.GetAsync(id, cancellationToken);
        return transaction is null ? Error.NotFound(StockMessages.MovementNotFound) : await DetailsAsync(uow, transaction, cancellationToken);
    }

    public async Task<Result<MovementDetails>> GetByNumberAsync(string number, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(number);
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<MovementDetails>.Failure(allowed.Errors);
        }

        var digits = number.Trim().TrimStart('M', 'm').TrimStart('-');
        if (!long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var sequence))
        {
            return Error.NotFound(StockMessages.MovementNotFound);
        }

        await using var uow = unitOfWorkFactory.Create();
        var transaction = await uow.Transactions.GetBySequenceAsync(sequence, cancellationToken);
        return transaction is null ? Error.NotFound(StockMessages.MovementNotFound) : await DetailsAsync(uow, transaction, cancellationToken);
    }

    private async Task<Result<MovementItem>> RecordItemMovementAsync(ItemMovementInput input, StockTransactionType type, CancellationToken cancellationToken)
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
                .When(input.Quantity <= 0, nameof(ItemMovementInput.Quantity), StockMessages.QuantityMustBePositive)
                .MaxLength(input.Lot, StockKey.LotMaxLength, nameof(ItemMovementInput.Lot), StockMessages.TextTooLong)
                .MaxLength(input.Reference, StockTransaction.ReferenceMaxLength, nameof(ItemMovementInput.Reference), StockMessages.TextTooLong)
                .MaxLength(input.Comment, StockTransaction.CommentMaxLength, nameof(ItemMovementInput.Comment), StockMessages.TextTooLong);
            var occurredAt = CheckDate(input.OccurredAt, validation);
            var item = await uow.Items.GetByIdAsync(input.ItemId, cancellationToken);
            if (item is null)
            {
                return Error.NotFound(StockMessages.ItemNotFound);
            }

            if (type == StockTransactionType.ItemReceipt && item.IsArchived)
            {
                validation.Add(nameof(ItemMovementInput.ItemId), StockMessages.ItemArchived);
            }

            validation
                .When(!StockFormat.FitsUnit(input.Quantity, item.Unit), nameof(ItemMovementInput.Quantity), StockMessages.QuantityPrecision)
                .When(item.TrackLots && string.IsNullOrWhiteSpace(input.Lot), nameof(ItemMovementInput.Lot), StockMessages.LotRequired);
            var location = await ResolveLocationAsync(uow, input.LocationId ?? item.DefaultLocationId, cancellationToken);
            if (location is null)
            {
                validation.Add(nameof(ItemMovementInput.LocationId), StockMessages.LocationNotFound);
            }

            if (!validation.IsValid)
            {
                return Result<LedgerEntry>.Failure(validation.Errors);
            }

            var entry = new LedgerEntry();
            var transaction = entry.Add(NewTransaction(type, occurredAt));
            transaction.Reference = Inputs.Clean(input.Reference);
            transaction.Comment = Inputs.Clean(input.Comment);
            var key = StockKey.ForItem(item.Id, location!.Id, item.TrackLots ? input.Lot : null);
            transaction.AddLine(key, type == StockTransactionType.ItemReceipt ? input.Quantity : -input.Quantity);
            return entry;
        }, cancellationToken);
        return await ToItemAsync(recorded, cancellationToken);
    }

    /// <summary>Emplacement demandé, sinon le magasin par défaut (<c>Stock:DefaultStorageLocationCode</c>).</summary>
    private async Task<StockLocation?> ResolveLocationAsync(IStockUnitOfWork uow, Guid? locationId, CancellationToken cancellationToken)
    {
        var location = locationId is { } id
            ? await uow.Locations.GetByIdAsync(id, cancellationToken)
            : await uow.Locations.GetByCodeAsync(options.DefaultStorageLocationCode, cancellationToken);
        return location is { IsArchived: false } ? location : null;
    }

    private static async Task<StockUnit?> UnitOfAsync(IStockUnitOfWork uow, StockKey key, CancellationToken cancellationToken)
    {
        if (!key.IsItem)
        {
            return StockUnit.Piece;
        }

        return (await uow.Items.GetByIdAsync(key.ItemId, cancellationToken))?.Unit;
    }

    /// <summary>Date du fait : maintenant par défaut, jamais dans le futur ni plus ancienne que <c>Stock:BackdatingDays</c>.</summary>
    private DateTimeOffset CheckDate(DateTimeOffset? requested, Validation validation)
    {
        var now = timeProvider.GetUtcNow();
        if (requested is not { } at)
        {
            return now;
        }

        // Une minute de tolérance : horloge du téléphone légèrement en avance.
        validation
            .When(at > now.AddMinutes(1), "OccurredAt", StockMessages.DateInFuture)
            .When(at < now.AddDays(-options.BackdatingDays), "OccurredAt",
                string.Format(CultureInfo.CurrentCulture, StockMessages.DateTooOld, options.BackdatingDays));
        // Toujours en UTC (PostgreSQL n'accepte que des dates UTC) ; une date à peine future devient « maintenant ».
        return at > now ? now : at.ToUniversalTime();
    }

    private StockTransaction NewTransaction(StockTransactionType type, DateTimeOffset occurredAt) =>
        new(type, occurredAt, timeProvider.GetUtcNow(), access.User.UserId, access.User.DisplayName ?? access.User.UserName);

    /// <summary>Raison pour laquelle le mouvement ne peut pas être annulé par l'utilisateur courant (null s'il le peut).</summary>
    private Error? ReversalRefusal(StockTransaction transaction)
    {
        if (transaction.Type == StockTransactionType.Reversal)
        {
            return Error.Validation(StockMessages.ReversalOfReversal);
        }

        if (transaction.IsReversed)
        {
            return Error.Validation(StockMessages.AlreadyReversed);
        }

        if (access.IsEditor)
        {
            return null;
        }

        var ownMovement = transaction.RecordedBy is { } author && author == access.User.UserId;
        if (!access.User.IsAuthenticated || !ownMovement)
        {
            return Error.Forbidden(StockMessages.ReversalForbidden);
        }

        return timeProvider.GetUtcNow() - transaction.RecordedAt > TimeSpan.FromMinutes(options.SelfUndoMinutes)
            ? Error.Forbidden(string.Format(CultureInfo.CurrentCulture, StockMessages.ReversalDelayExpired, options.SelfUndoMinutes))
            : null;
    }

    private async Task<Result<MovementItem>> ToItemAsync(Result<IReadOnlyList<StockTransaction>> recorded, CancellationToken cancellationToken)
    {
        if (recorded.IsFailure)
        {
            return Result<MovementItem>.Failure(recorded.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        return (await DescribeAsync(uow, recorded.Value, cancellationToken))[0];
    }

    private async Task<Result<MovementDetails>> DetailsAsync(IStockUnitOfWork uow, StockTransaction transaction, CancellationToken cancellationToken)
    {
        var item = (await DescribeAsync(uow, [transaction], cancellationToken))[0];
        var refusal = ReversalRefusal(transaction);
        return new MovementDetails(item, refusal is null, refusal?.Message);
    }

    /// <summary>Mouvements lisibles : noms des objets et emplacements, numéros des mouvements liés.</summary>
    internal async Task<IReadOnlyList<MovementItem>> DescribeAsync(IStockUnitOfWork uow, IReadOnlyList<StockTransaction> transactions, CancellationToken cancellationToken)
    {
        var names = await StockNames.LoadAsync(uow, parts, transactions.SelectMany(t => t.Lines).Select(l => l.Key), cancellationToken);
        var linkedIds = transactions.SelectMany(t => new[] { t.ReversesTransactionId, t.ReversedByTransactionId }).OfType<Guid>().Distinct().ToList();
        var sequences = linkedIds.Count == 0 ? new Dictionary<Guid, long>() : await uow.Transactions.GetSequencesAsync(linkedIds, cancellationToken);
        string? NumberOf(Guid? id) => id is { } value && sequences.TryGetValue(value, out var sequence) ? StockTransaction.FormatNumber(sequence) : null;

        return transactions.Select(t => new MovementItem(
            t.Id, t.Number, t.Type, t.OccurredAt, t.RecordedAt, t.RecordedByName, t.BatchId, t.Reference, t.Reason, t.Comment,
            t.ReversesTransactionId, NumberOf(t.ReversesTransactionId), t.ReversedByTransactionId, NumberOf(t.ReversedByTransactionId),
            t.Lines.Select(l => new MovementLineView(l.Key, names.SubjectCode(l.Key), names.SubjectLabel(l.Key), names.Unit(l.Key),
                names.LocationCode(l.LocationId), null, l.Delta)).ToList())).ToList();
    }

    private static DateTimeOffset StartOfDay(DateOnly date, TimeZoneInfo zone)
    {
        var local = date.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }
}
