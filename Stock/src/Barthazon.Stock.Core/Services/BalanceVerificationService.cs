using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Persistence;

namespace Barthazon.Stock.Core.Services;

/// <summary>Outil d'administration « Vérifier les soldes » : soldes enregistrés comparés à la somme des lignes de mouvements.</summary>
public interface IBalanceVerificationService
{
    /// <summary>Écarts entre les soldes et les mouvements (administrateur).</summary>
    Task<Result<IReadOnlyList<BalanceDiscrepancy>>> CheckAsync(CancellationToken cancellationToken = default);

    /// <summary>Recalcule les soldes à partir des mouvements (administrateur). Renvoie le nombre de cases corrigées.</summary>
    Task<Result<int>> RepairAsync(CancellationToken cancellationToken = default);
}

internal sealed class BalanceVerificationService(IUnitOfWorkFactory<IStockUnitOfWork> unitOfWorkFactory, StockAccess access) : IBalanceVerificationService
{
    public async Task<Result<IReadOnlyList<BalanceDiscrepancy>>> CheckAsync(CancellationToken cancellationToken = default)
    {
        var allowed = access.CanAdminister();
        if (allowed.IsFailure)
        {
            return Result<IReadOnlyList<BalanceDiscrepancy>>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        return Result.Success(await FindDiscrepanciesAsync(uow, cancellationToken));
    }

    public async Task<Result<int>> RepairAsync(CancellationToken cancellationToken = default)
    {
        var allowed = access.CanAdminister();
        if (allowed.IsFailure)
        {
            return Result<int>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var discrepancies = await FindDiscrepanciesAsync(uow, cancellationToken);
        if (discrepancies.Count == 0)
        {
            return 0;
        }

        var balances = (await uow.Balances.GetForUpdateAsync(discrepancies.Select(d => d.Key).ToList(), cancellationToken)).ToDictionary(b => b.Key);
        foreach (var discrepancy in discrepancies)
        {
            if (balances.TryGetValue(discrepancy.Key, out var balance))
            {
                if (discrepancy.ComputedQuantity == 0)
                {
                    uow.Balances.Remove(balance);
                }
                else
                {
                    balance.Reset(discrepancy.ComputedQuantity);
                }
            }
            else
            {
                var created = new StockBalance(discrepancy.Key);
                created.Reset(discrepancy.ComputedQuantity);
                uow.Balances.Add(created);
            }
        }

        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? discrepancies.Count : Result<int>.Failure(saved.Errors);
    }

    internal static async Task<IReadOnlyList<BalanceDiscrepancy>> FindDiscrepanciesAsync(IStockUnitOfWork uow, CancellationToken cancellationToken)
    {
        var computed = (await uow.Transactions.ListAllLinesAsync(cancellationToken))
            .GroupBy(l => l.Key)
            .ToDictionary(g => g.Key, g => g.Sum(l => l.Delta));
        var recorded = (await uow.Balances.ListAsync(new BalanceFilter(), cancellationToken)).ToDictionary(b => b.Key, b => b.Quantity);
        return computed.Keys.Union(recorded.Keys)
            .Select(key => new BalanceDiscrepancy(key, recorded.GetValueOrDefault(key), computed.GetValueOrDefault(key)))
            .Where(d => d.RecordedQuantity != d.ComputedQuantity)
            .ToList();
    }
}
