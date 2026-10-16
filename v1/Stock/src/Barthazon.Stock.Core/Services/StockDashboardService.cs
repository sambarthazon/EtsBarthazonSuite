using Barthazon.Catalog.Contracts;
using Barthazon.Referential.Contracts;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Stock.Contracts;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Persistence;

namespace Barthazon.Stock.Core.Services;

/// <summary>Pièces qui attendent une opération sur une machine (file d'attente).</summary>
public sealed record MachineQueueLine(Guid BatchId, string BatchNumber, string PartReference, int OperationNumber, OperationType OperationType, int Quantity, decimal? LoadMinutes);

/// <summary>File d'attente d'une machine, de la sous-traitance ou des opérations sans machine, avec la charge estimée.</summary>
public sealed record MachineQueue(Guid? MachineId, string Name, bool IsSubcontracting, int Quantity, decimal? LoadMinutes, IReadOnlyList<MachineQueueLine> Lines);

/// <summary>Tableau de bord du stock (section 10.1).</summary>
public sealed record StockDashboard(
    IReadOnlyList<ItemListItem> ItemsBelowThreshold,
    IReadOnlyList<TrackedPartListItem> PartsBelowThreshold,
    IReadOnlyList<TrackedPartListItem> PartsWithSupersededStock,
    IReadOnlyList<BatchListItem> StaleBatches,
    IReadOnlyList<BatchListItem> BatchesOnSupersededRevision,
    int? InconsistentBalances,
    IReadOnlyList<MachineQueue> Queues,
    IReadOnlyList<BatchListItem> BatchesInProgress,
    int ItemCount,
    int TrackedPartCount,
    decimal MaterialValue)
{
    public int AlertCount => ItemsBelowThreshold.Count + PartsBelowThreshold.Count + PartsWithSupersededStock.Count + StaleBatches.Count
        + BatchesOnSupersededRevision.Count + (InconsistentBalances ?? 0);
}

public interface IStockDashboardService
{
    Task<Result<StockDashboard>> GetAsync(CancellationToken cancellationToken = default);
}

internal sealed class StockDashboardService(
    IUnitOfWorkFactory<IStockUnitOfWork> unitOfWorkFactory,
    StockAccess access,
    IItemService items,
    ITrackedPartService trackedParts,
    IBatchService batches,
    IMachineLookup machines) : IStockDashboardService
{
    public async Task<Result<StockDashboard>> GetAsync(CancellationToken cancellationToken = default)
    {
        var allowed = access.CanRead();
        if (allowed.IsFailure)
        {
            return Result<StockDashboard>.Failure(allowed.Errors);
        }

        var allItems = (await items.SearchAsync(new ItemSearchRequest(), cancellationToken)).Value;
        var parts = (await trackedParts.SearchAsync(new TrackedPartSearchRequest(), cancellationToken)).Value;
        var open = (await batches.SearchAsync(new BatchSearchRequest { Status = BatchStatus.InProgress }, cancellationToken)).Value;

        // Vérification des soldes : seulement pour un administrateur (calcul complet des mouvements).
        int? inconsistent = null;
        if (access.CanAdminister().IsSuccess)
        {
            await using var uow = unitOfWorkFactory.Create();
            inconsistent = (await BalanceVerificationService.FindDiscrepanciesAsync(uow, cancellationToken)).Count;
        }

        return new StockDashboard(
            allItems.Where(i => i.IsBelowThreshold).ToList(),
            parts.Where(p => p.IsBelowThreshold).ToList(),
            parts.Where(p => p.HasSupersededStock).ToList(),
            open.Where(b => b.IsStale).ToList(),
            open.Where(b => !b.RevisionIsInForce).ToList(),
            inconsistent,
            await QueuesAsync(open, cancellationToken),
            open,
            allItems.Count,
            parts.Count,
            allItems.Sum(i => i.Value ?? 0));
    }

    /// <summary>
    /// Files d'attente : pièces en attente d'une opération, regroupées par machine (« Sous-traitance », « Sans machine »).
    /// Charge estimée = Σ quantité × temps unitaire + temps de réglage par lancement, si les temps sont connus.
    /// </summary>
    private async Task<IReadOnlyList<MachineQueue>> QueuesAsync(IReadOnlyList<BatchListItem> open, CancellationToken cancellationToken)
    {
        var lines = new List<(Guid? MachineId, bool Subcontracting, MachineQueueLine Line)>();
        foreach (var batch in open)
        {
            var details = await batches.GetAsync(batch.Id, cancellationToken);
            if (details.IsFailure)
            {
                continue;
            }

            foreach (var step in details.Value.Steps.Where(s => s.Waiting > 0))
            {
                decimal? load = step.CycleTimeMin is null && step.SetupTimeMin is null
                    ? null
                    : (step.CycleTimeMin ?? 0) * step.Waiting + (step.SetupTimeMin ?? 0);
                var subcontracting = step.OperationType == OperationType.Subcontracting || step.Subcontractor is not null;
                lines.Add((subcontracting ? null : step.MachineId, subcontracting,
                    new MachineQueueLine(batch.Id, batch.Number, batch.PartReference, step.OperationNumber, step.OperationType, step.Waiting, load)));
            }
        }

        var names = await machines.GetManyAsync(lines.Where(l => l.MachineId is not null).Select(l => l.MachineId!.Value).Distinct(), cancellationToken);
        return lines
            .GroupBy(l => (l.MachineId, l.Subcontracting))
            .Select(g => new MachineQueue(
                g.Key.MachineId,
                g.Key.Subcontracting ? Resources.StockMessages.QueueSubcontracting
                    : g.Key.MachineId is { } id && names.TryGetValue(id, out var machine) ? machine.Name : Resources.StockMessages.QueueNoMachine,
                g.Key.Subcontracting,
                g.Sum(l => l.Line.Quantity),
                g.Any(l => l.Line.LoadMinutes is not null) ? g.Sum(l => l.Line.LoadMinutes ?? 0) : null,
                g.Select(l => l.Line).OrderBy(l => l.BatchNumber, StringComparer.Ordinal).ToList()))
            .OrderBy(q => q.MachineId is null ? 1 : 0).ThenBy(q => q.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
