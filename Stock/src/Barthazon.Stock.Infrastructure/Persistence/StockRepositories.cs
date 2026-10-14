using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Stock.Infrastructure.Persistence;

internal sealed class StockLocationRepository(StockDbContext context) : EfRepository<StockLocation>(context), IStockLocationRepository
{
    public async Task<IReadOnlyList<StockLocation>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().Where(l => includeArchived || !l.IsArchived)
            .OrderBy(l => l.SortOrder).ThenBy(l => l.Code).ToListAsync(cancellationToken);

    public Task<StockLocation?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = StockLocation.NormalizeCode(code);
        return Set.FirstOrDefaultAsync(l => l.Code == normalized, cancellationToken);
    }

    public Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var normalized = StockLocation.NormalizeCode(code);
        return Set.AnyAsync(l => l.Code == normalized && (excludeId == null || l.Id != excludeId), cancellationToken);
    }
}

internal sealed class StockItemRepository(StockDbContext context) : EfRepository<StockItem>(context), IStockItemRepository
{
    public async Task<IReadOnlyList<StockItem>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().Where(i => includeArchived || !i.IsArchived).OrderBy(i => i.Code).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<StockItem>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var list = ids.Distinct().ToList();
        return await Set.AsNoTracking().Where(i => list.Contains(i.Id)).ToListAsync(cancellationToken);
    }

    public Task<StockItem?> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var normalized = StockItem.NormalizeCode(code);
        return Set.FirstOrDefaultAsync(i => i.Code == normalized, cancellationToken);
    }

    public Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default)
    {
        var normalized = StockItem.NormalizeCode(code);
        return Set.AnyAsync(i => i.Code == normalized && (excludeId == null || i.Id != excludeId), cancellationToken);
    }

    public async Task<IReadOnlyList<string>> ListCodesAsync(CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().Select(i => i.Code).ToListAsync(cancellationToken);
}

internal sealed class StockTransactionRepository(StockDbContext context) : IStockTransactionRepository
{
    private DbSet<StockTransaction> Set => context.Set<StockTransaction>();

    public void Add(StockTransaction transaction) => Set.Add(transaction);

    public Task<StockTransaction?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Set.Include(t => t.Lines).FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public Task<StockTransaction?> GetBySequenceAsync(long sequence, CancellationToken cancellationToken = default) =>
        Set.Include(t => t.Lines).FirstOrDefaultAsync(t => t.Sequence == sequence, cancellationToken);

    public async Task<long> GetLastSequenceAsync(CancellationToken cancellationToken = default) =>
        await Set.MaxAsync(t => (long?)t.Sequence, cancellationToken) ?? 0;

    public async Task<PagedResult<StockTransaction>> SearchAsync(MovementFilter filter, PageRequest page, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        ArgumentNullException.ThrowIfNull(page);
        var query = Set.AsNoTracking();
        if (filter.From is { } from)
        {
            query = query.Where(t => t.OccurredAt >= from);
        }

        if (filter.To is { } to)
        {
            query = query.Where(t => t.OccurredAt < to);
        }

        if (filter.Types is { Count: > 0 } types)
        {
            query = query.Where(t => types.Contains(t.Type));
        }

        if (filter.BatchId is { } batchId)
        {
            query = query.Where(t => t.BatchId == batchId || t.Lines.Any(l => l.BatchId == batchId));
        }

        if (filter.ItemId is { } itemId)
        {
            query = query.Where(t => t.Lines.Any(l => l.ItemId == itemId));
        }

        if (filter.PartId is { } partId)
        {
            query = query.Where(t => t.Lines.Any(l => l.PartId == partId));
        }

        if (filter.LocationId is { } locationId)
        {
            query = query.Where(t => t.Lines.Any(l => l.LocationId == locationId));
        }

        if (filter.UserName is { } userName)
        {
            query = query.Where(t => t.RecordedByName == userName);
        }

        if (!filter.IncludeReversed)
        {
            query = query.Where(t => t.ReversedByTransactionId == null && t.ReversesTransactionId == null);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(t => t.Sequence).Skip(page.Skip).Take(page.PageSize)
            .Include(t => t.Lines).AsSplitQuery().ToListAsync(cancellationToken);
        return new PagedResult<StockTransaction>(items, total);
    }

    public async Task<IReadOnlyDictionary<Guid, long>> GetSequencesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var list = ids.Distinct().ToList();
        return await Set.AsNoTracking().Where(t => list.Contains(t.Id)).ToDictionaryAsync(t => t.Id, t => t.Sequence, cancellationToken);
    }

    public async Task<StockTransaction?> FindLatestConsumerAsync(StockKey key, long afterSequence, CancellationToken cancellationToken = default)
    {
        var lines = context.Set<StockTransactionLine>().AsNoTracking()
            .Where(l => l.SubjectKind == key.SubjectKind && l.Bucket == key.Bucket && l.LocationId == key.LocationId && l.Delta < 0);
        lines = key.IsItem
            ? lines.Where(l => l.ItemId == key.ItemId)
            : lines.Where(l => l.PartId == key.PartId && l.RevisionId == key.RevisionId);
        if (key.IsWorkInProgress)
        {
            lines = lines.Where(l => l.BatchId == key.BatchId && l.Stage == key.Stage);
        }

        var lot = key.Lot.Length == 0 ? null : key.Lot;
        lines = lines.Where(l => l.Lot == lot);
        var transactionIds = lines.Select(l => l.TransactionId);
        return await Set.AsNoTracking()
            .Where(t => transactionIds.Contains(t.Id) && t.Sequence > afterSequence && t.ReversedByTransactionId == null && t.Type != StockTransactionType.Reversal)
            .OrderByDescending(t => t.Sequence)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StockTransactionLine>> ListAllLinesAsync(CancellationToken cancellationToken = default) =>
        await context.Set<StockTransactionLine>().AsNoTracking().ToListAsync(cancellationToken);
}

internal sealed class StockBalanceRepository(StockDbContext context) : IStockBalanceRepository
{
    private DbSet<StockBalance> Set => context.Set<StockBalance>();

    public async Task<IReadOnlyList<StockBalance>> GetForUpdateAsync(IReadOnlyCollection<StockKey> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);
        if (keys.Count == 0)
        {
            return [];
        }

        // Pré-filtre en base sur les identifiants, puis comparaison exacte des clés en mémoire.
        var locations = keys.Select(k => k.LocationId).Distinct().ToList();
        var items = keys.Select(k => k.ItemId).Distinct().ToList();
        var partIds = keys.Select(k => k.PartId).Distinct().ToList();
        var candidates = await Set.Where(b => locations.Contains(b.LocationId) && items.Contains(b.ItemId) && partIds.Contains(b.PartId))
            .ToListAsync(cancellationToken);
        var wanted = keys.ToHashSet();
        return candidates.Where(b => wanted.Contains(b.Key)).ToList();
    }

    public async Task<IReadOnlyList<StockBalance>> ListAsync(BalanceFilter filter, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(filter);
        var query = Set.AsNoTracking();
        if (filter.SubjectKind is { } subjectKind)
        {
            query = query.Where(b => b.SubjectKind == subjectKind);
        }

        if (filter.Bucket is { } bucket)
        {
            query = query.Where(b => b.Bucket == bucket);
        }

        if (filter.ItemId is { } itemId)
        {
            query = query.Where(b => b.ItemId == itemId);
        }

        if (filter.ItemIds is { } itemIds)
        {
            query = query.Where(b => itemIds.Contains(b.ItemId));
        }

        if (filter.PartId is { } partId)
        {
            query = query.Where(b => b.PartId == partId);
        }

        if (filter.PartIds is { } partIds)
        {
            query = query.Where(b => partIds.Contains(b.PartId));
        }

        if (filter.BatchId is { } batchId)
        {
            query = query.Where(b => b.BatchId == batchId);
        }

        if (filter.LocationId is { } locationId)
        {
            query = query.Where(b => b.LocationId == locationId);
        }

        return await query.ToListAsync(cancellationToken);
    }

    public Task<bool> AnyAtLocationAsync(Guid locationId, CancellationToken cancellationToken = default) =>
        Set.AnyAsync(b => b.LocationId == locationId, cancellationToken);

    public void Add(StockBalance balance) => Set.Add(balance);

    public void Remove(StockBalance balance) => Set.Remove(balance);
}

internal sealed class TrackedPartRepository(StockDbContext context) : EfRepository<TrackedPart>(context), ITrackedPartRepository
{
    public Task<TrackedPart?> GetByPartIdAsync(Guid partId, CancellationToken cancellationToken = default) =>
        Set.Include(t => t.Sources).FirstOrDefaultAsync(t => t.PartId == partId, cancellationToken);

    public async Task<IReadOnlyList<TrackedPart>> ListAsync(CancellationToken cancellationToken = default) =>
        await Set.AsNoTracking().Include(t => t.Sources).ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<TrackedPart>> GetManyAsync(IEnumerable<Guid> partIds, CancellationToken cancellationToken = default)
    {
        var list = partIds.Distinct().ToList();
        return await Set.AsNoTracking().Include(t => t.Sources).Where(t => list.Contains(t.PartId)).ToListAsync(cancellationToken);
    }

    public async Task<TrackedPart?> FindByLastKnownReferenceAsync(string reference, CancellationToken cancellationToken = default)
    {
        var candidates = await Set.AsNoTracking().Where(t => t.LastKnownReference == reference.Trim()).ToListAsync(cancellationToken);
        return candidates.FirstOrDefault();
    }

    public void AddSource(BlankSource source) => Context.Set<BlankSource>().Add(source);

    public void RemoveSource(BlankSource source) => Context.Set<BlankSource>().Remove(source);

    public async Task<IReadOnlyList<(Guid PartId, BlankSource Source)>> ListSourcesAsync(CancellationToken cancellationToken = default)
    {
        var rows = await Context.Set<BlankSource>().AsNoTracking()
            .Join(Set.AsNoTracking(), s => s.TrackedPartId, t => t.Id, (s, t) => new { t.PartId, Source = s })
            .ToListAsync(cancellationToken);
        return rows.Select(r => (r.PartId, r.Source)).ToList();
    }
}

internal sealed class StockUnitOfWork : EfUnitOfWork, IStockUnitOfWork
{
    public StockUnitOfWork(StockDbContext context, string? userName)
        : base(context, userName)
    {
        Locations = new StockLocationRepository(context);
        Items = new StockItemRepository(context);
        Transactions = new StockTransactionRepository(context);
        Balances = new StockBalanceRepository(context);
        TrackedParts = new TrackedPartRepository(context);
    }

    public IStockLocationRepository Locations { get; }

    public IStockItemRepository Items { get; }

    public IStockTransactionRepository Transactions { get; }

    public IStockBalanceRepository Balances { get; }

    public ITrackedPartRepository TrackedParts { get; }
}

/// <summary>Fabrique d'unités de travail du stock (une par opération applicative).</summary>
internal sealed class StockUnitOfWorkFactory(IDbContextFactory<StockDbContext> contextFactory, ICurrentUser currentUser)
    : IUnitOfWorkFactory<IStockUnitOfWork>
{
    public IStockUnitOfWork Create() => new StockUnitOfWork(contextFactory.CreateDbContext(), currentUser.UserName);
}
