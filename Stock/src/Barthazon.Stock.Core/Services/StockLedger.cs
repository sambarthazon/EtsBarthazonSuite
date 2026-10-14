using System.Globalization;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Persistence;
using Barthazon.Stock.Core.Resources;

namespace Barthazon.Stock.Core.Services;

/// <summary>Mouvements préparés par un service, enregistrés ensemble par <see cref="StockLedger"/>.</summary>
internal sealed class LedgerEntry
{
    public List<StockTransaction> Transactions { get; } = [];

    /// <summary>Action à exécuter dans la même unité de travail juste avant l'enregistrement (ex. marquer un mouvement annulé).</summary>
    public List<Action> BeforeSave { get; } = [];

    public StockTransaction Add(StockTransaction transaction)
    {
        Transactions.Add(transaction);
        return transaction;
    }
}

/// <summary>
/// Enregistre des mouvements : numérotation, mise à jour des soldes dans la même transaction, contrôle des quantités
/// négatives, tout ou rien. En cas de conflit (deux saisies simultanées sur la même case), relit et revalide une fois.
/// </summary>
internal sealed class StockLedger(IUnitOfWorkFactory<IStockUnitOfWork> unitOfWorkFactory, StockOptions options, TimeProvider timeProvider)
{
    /// <summary>
    /// Exécute <paramref name="prepare"/> dans une unité de travail neuve (lecture, contrôles, préparation des
    /// mouvements), puis enregistre. <paramref name="prepare"/> est rappelée en cas de nouvel essai.
    /// </summary>
    public async Task<Result<IReadOnlyList<StockTransaction>>> RecordAsync(
        Func<IStockUnitOfWork, Task<Result<LedgerEntry>>> prepare, CancellationToken cancellationToken = default)
    {
        Result<IReadOnlyList<StockTransaction>>? last = null;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            await using var uow = unitOfWorkFactory.Create();
            var prepared = await prepare(uow);
            if (prepared.IsFailure)
            {
                return Result<IReadOnlyList<StockTransaction>>.Failure(prepared.Errors);
            }

            var entry = prepared.Value;
            var applied = await ApplyAsync(uow, entry, cancellationToken);
            if (applied.IsFailure)
            {
                return Result<IReadOnlyList<StockTransaction>>.Failure(applied.Errors);
            }

            foreach (var action in entry.BeforeSave)
            {
                action();
            }

            var saved = await uow.SaveChangesAsync(cancellationToken);
            if (saved.IsSuccess)
            {
                return Result.Success<IReadOnlyList<StockTransaction>>(entry.Transactions);
            }

            last = Result<IReadOnlyList<StockTransaction>>.Failure(saved.Errors);
            if (!saved.HasError(ErrorKind.Conflict))
            {
                break;
            }
        }

        return last!.HasError(ErrorKind.Conflict)
            ? Result<IReadOnlyList<StockTransaction>>.Failure(Error.Conflict(StockMessages.MovementConflict))
            : last;
    }

    private async Task<Result> ApplyAsync(IStockUnitOfWork uow, LedgerEntry entry, CancellationToken cancellationToken)
    {
        var lines = entry.Transactions.SelectMany(t => t.Lines).ToList();
        if (lines.Count == 0)
        {
            return Result.Failure(Error.Validation(StockMessages.MovementEmpty));
        }

        var keys = lines.Select(l => l.Key).Distinct().ToList();
        var balances = (await uow.Balances.GetForUpdateAsync(keys, cancellationToken)).ToDictionary(b => b.Key);
        var errors = new List<Error>();
        foreach (var group in lines.GroupBy(l => l.Key))
        {
            var key = group.Key;
            var delta = group.Sum(l => l.Delta);
            var at = entry.Transactions.Where(t => t.Lines.Any(l => l.Key == key)).Max(t => t.OccurredAt);
            balances.TryGetValue(key, out var balance);
            var current = balance?.Quantity ?? 0m;
            var next = current + delta;
            if (next < 0 && delta < 0 && !(key.IsItem && options.AllowNegativeItemStock))
            {
                var message = key.IsItem ? StockMessages.InsufficientItemQuantity
                    : key.IsWorkInProgress ? StockMessages.InsufficientWorkInProgress
                    : StockMessages.InsufficientPartQuantity;
                errors.Add(Error.Validation(string.Format(CultureInfo.CurrentCulture, message, StockFormat.Quantity(Math.Max(current, 0)))));
                continue;
            }

            if (balance is null)
            {
                balance = new StockBalance(key);
                uow.Balances.Add(balance);
                balances[key] = balance;
            }

            balance.Apply(delta, at);
            if (balance.Quantity == 0)
            {
                uow.Balances.Remove(balance);
            }
        }

        if (errors.Count > 0)
        {
            return Result.Failure(errors.Distinct());
        }

        await UpdateBatchStatusesAsync(uow, balances.Values, cancellationToken);

        var sequence = await uow.Transactions.GetLastSequenceAsync(cancellationToken);
        foreach (var transaction in entry.Transactions)
        {
            transaction.Sequence = ++sequence;
            uow.Transactions.Add(transaction);
        }

        return Result.Success();
    }

    /// <summary>
    /// Statut automatique des lancements touchés : terminé quand plus aucune pièce n'y est en cours, de nouveau en
    /// cours si une annulation en remet (section 8.5). Calculé dans la même transaction que les mouvements.
    /// </summary>
    private async Task UpdateBatchStatusesAsync(IStockUnitOfWork uow, IEnumerable<StockBalance> changed, CancellationToken cancellationToken)
    {
        var changedList = changed.Where(b => b.Bucket == StockBucket.WorkInProgress).ToList();
        foreach (var batchId in changedList.Select(b => b.BatchId).Distinct())
        {
            var batch = await uow.Batches.GetByIdAsync(batchId, cancellationToken);
            if (batch is null)
            {
                continue; // Lancement créé dans cette même saisie : il démarre « en cours ».
            }

            // Soldes en base (valeurs suivies, donc à jour des variations de cette saisie) + soldes créés par la saisie.
            var stored = await uow.Balances.ListAsync(new BalanceFilter { Bucket = StockBucket.WorkInProgress, BatchId = batchId }, cancellationToken);
            var current = changedList.Where(b => b.BatchId == batchId).ToDictionary(b => b.Id, b => b.Quantity);
            foreach (var balance in stored.Where(b => !current.ContainsKey(b.Id)))
            {
                current[balance.Id] = balance.Quantity;
            }

            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(timeProvider.GetUtcNow(), timeProvider.LocalTimeZone).DateTime);
            batch.UpdateStatus(current.Values.Where(q => q > 0).Sum(), today);
        }
    }
}
