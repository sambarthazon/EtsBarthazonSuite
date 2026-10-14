using Barthazon.Catalog.Contracts;
using Barthazon.Referential.Contracts;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Persistence;
using Barthazon.Stock.Core.Resources;

namespace Barthazon.Stock.Core.Services;

/// <summary>
/// Stock d'une pièce pour l'annuaire (section 9.2) : pièces finies en stock (tous indices), en « pce », emplacement s'il
/// est unique, seuil de la pièce suivie, pas de valeur. Rien pour une pièce non suivie.
/// </summary>
internal sealed class PartStockInfoProvider(IUnitOfWorkFactory<IStockUnitOfWork> unitOfWorkFactory, StockAccess access) : IPartStockInfoProvider
{
    public bool IsAvailable => true;

    public async Task<PartStockInfo?> GetAsync(Guid partId, CancellationToken cancellationToken = default) =>
        (await GetManyAsync([partId], cancellationToken)).GetValueOrDefault(partId);

    public async Task<IReadOnlyDictionary<Guid, PartStockInfo>> GetManyAsync(IEnumerable<Guid> partIds, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(partIds);
        var ids = partIds.Distinct().ToList();
        if (ids.Count == 0 || access.CanRead().IsFailure)
        {
            return new Dictionary<Guid, PartStockInfo>();
        }

        await using var uow = unitOfWorkFactory.Create();
        var tracked = await uow.TrackedParts.GetManyAsync(ids, cancellationToken);
        if (tracked.Count == 0)
        {
            return new Dictionary<Guid, PartStockInfo>();
        }

        var finished = (await uow.Balances.ListAsync(new BalanceFilter
        {
            SubjectKind = StockSubjectKind.Part, Bucket = StockBucket.Stock, PartIds = tracked.Select(t => t.PartId).ToList(),
        }, cancellationToken)).Where(b => b.Quantity > 0).ToLookup(b => b.PartId);
        var locations = (await uow.Locations.ListAsync(includeArchived: true, cancellationToken)).ToDictionary(l => l.Id);
        return tracked.ToDictionary(t => t.PartId, t =>
        {
            var cells = finished[t.PartId].ToList();
            var places = cells.Select(c => c.LocationId).Distinct().ToList();
            var location = places.Count switch
            {
                0 => null,
                1 => locations.TryGetValue(places[0], out var place) ? place.Name : null,
                _ => StockMessages.SeveralLocations,
            };
            return new PartStockInfo(cells.Sum(c => c.Quantity), StockMessages.PieceUnit, location, t.MinimumThreshold, null);
        });
    }
}

/// <summary>Filtres « En rupture » et « Sous le seuil » de la liste des pièces de l'annuaire (pièces suivies seulement).</summary>
internal sealed class PartStockFilterProvider(IUnitOfWorkFactory<IStockUnitOfWork> unitOfWorkFactory, StockAccess access) : IPartStockFilterProvider
{
    public bool IsAvailable => true;

    public async Task<IReadOnlySet<Guid>> GetPartIdsAsync(PartStockFilter filter, CancellationToken cancellationToken = default)
    {
        if (access.CanRead().IsFailure)
        {
            return new HashSet<Guid>();
        }

        await using var uow = unitOfWorkFactory.Create();
        var tracked = await uow.TrackedParts.ListAsync(cancellationToken);
        var finished = (await uow.Balances.ListAsync(new BalanceFilter { SubjectKind = StockSubjectKind.Part, Bucket = StockBucket.Stock }, cancellationToken))
            .GroupBy(b => b.PartId).ToDictionary(g => g.Key, g => g.Sum(b => b.Quantity));
        return tracked
            .Where(t => filter == PartStockFilter.OutOfStock
                ? finished.GetValueOrDefault(t.PartId) <= 0
                : t.MinimumThreshold is { } threshold && finished.GetValueOrDefault(t.PartId) < threshold)
            .Select(t => t.PartId)
            .ToHashSet();
    }
}

/// <summary>
/// Usages du référentiel par le stock (section 9.2) : matières et clients propriétaires des articles, machines des étapes
/// de lancement. Un élément utilisé ne peut plus être supprimé, seulement archivé.
/// </summary>
internal sealed class StockReferentialUsageProvider(IUnitOfWorkFactory<IStockUnitOfWork> unitOfWorkFactory) : IReferentialUsageProvider
{
    public async Task<IReadOnlyDictionary<Guid, int>> CountUsagesAsync(ReferentialItemKind kind, CancellationToken cancellationToken = default)
    {
        await using var uow = unitOfWorkFactory.Create();
        IEnumerable<Guid?> ids = kind switch
        {
            ReferentialItemKind.Material => (await uow.Items.ListAsync(includeArchived: true, cancellationToken)).Select(i => i.MaterialId),
            ReferentialItemKind.Customer => (await uow.Items.ListAsync(includeArchived: true, cancellationToken)).Select(i => i.OwnerCustomerId),
            ReferentialItemKind.Machine => (await uow.Batches.ListAsync(new BatchFilter(), cancellationToken)).SelectMany(b => b.Steps).Select(s => s.MachineId),
            _ => [],
        };
        return ids.OfType<Guid>().GroupBy(id => id).ToDictionary(g => g.Key, g => g.Count());
    }
}
