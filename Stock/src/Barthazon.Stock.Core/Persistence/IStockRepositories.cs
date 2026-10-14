using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Stock.Core.Domain;

namespace Barthazon.Stock.Core.Persistence;

public interface IStockLocationRepository : IRepository<StockLocation>
{
    Task<IReadOnlyList<StockLocation>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    Task<StockLocation?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default);
}

public interface IStockItemRepository : IRepository<StockItem>
{
    Task<IReadOnlyList<StockItem>> ListAsync(bool includeArchived, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StockItem>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    Task<StockItem?> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<bool> CodeExistsAsync(string code, Guid? excludeId = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<string>> ListCodesAsync(CancellationToken cancellationToken = default);

}

public interface ITrackedPartRepository : IRepository<TrackedPart>
{
    /// <summary>Pièce suivie et ses sources (avec suivi des modifications).</summary>
    Task<TrackedPart?> GetByPartIdAsync(Guid partId, CancellationToken cancellationToken = default);

    /// <summary>Toutes les pièces suivies et leurs sources (sans suivi).</summary>
    Task<IReadOnlyList<TrackedPart>> ListAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TrackedPart>> GetManyAsync(IEnumerable<Guid> partIds, CancellationToken cancellationToken = default);

    /// <summary>Pièce suivie dont la dernière référence connue est celle-ci (pièce devenue introuvable dans l'annuaire).</summary>
    Task<TrackedPart?> FindByLastKnownReferenceAsync(string reference, CancellationToken cancellationToken = default);

    /// <summary>Nouvelle source d'une pièce suivie déjà enregistrée.</summary>
    void AddSource(BlankSource source);

    void RemoveSource(BlankSource source);

    /// <summary>Toutes les sources de brut (sans suivi), avec la pièce qui les déclare.</summary>
    Task<IReadOnlyList<(Guid PartId, BlankSource Source)>> ListSourcesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Critères de recherche dans le journal des mouvements.</summary>
public sealed record MovementFilter
{
    public DateTimeOffset? From { get; init; }

    public DateTimeOffset? To { get; init; }

    public IReadOnlyCollection<StockTransactionType>? Types { get; init; }

    public Guid? ItemId { get; init; }

    public Guid? PartId { get; init; }

    public Guid? BatchId { get; init; }

    public Guid? LocationId { get; init; }

    public string? UserName { get; init; }

    /// <summary>Faux : masque les mouvements annulés et les annulations.</summary>
    public bool IncludeReversed { get; init; } = true;
}

public interface IStockTransactionRepository
{
    void Add(StockTransaction transaction);

    /// <summary>Mouvement avec ses lignes (suivi des modifications : seul « annulé par » peut changer).</summary>
    Task<StockTransaction?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<StockTransaction?> GetBySequenceAsync(long sequence, CancellationToken cancellationToken = default);

    Task<long> GetLastSequenceAsync(CancellationToken cancellationToken = default);

    /// <summary>Mouvements (avec leurs lignes, sans suivi), du plus récent au plus ancien.</summary>
    Task<PagedResult<StockTransaction>> SearchAsync(MovementFilter filter, PageRequest page, CancellationToken cancellationToken = default);

    /// <summary>Numéros des mouvements donnés.</summary>
    Task<IReadOnlyDictionary<Guid, long>> GetSequencesAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dernier mouvement non annulé, postérieur à <paramref name="afterSequence"/>, qui a retiré des quantités de cette case
    /// (pour expliquer pourquoi une annulation est impossible).
    /// </summary>
    Task<StockTransaction?> FindLatestConsumerAsync(StockKey key, long afterSequence, CancellationToken cancellationToken = default);

    /// <summary>Toutes les lignes (clé et variation), pour recalculer les soldes.</summary>
    Task<IReadOnlyList<StockTransactionLine>> ListAllLinesAsync(CancellationToken cancellationToken = default);
}

/// <summary>Critères de lecture des soldes (tous facultatifs).</summary>
public sealed record BalanceFilter
{
    public StockSubjectKind? SubjectKind { get; init; }

    public StockBucket? Bucket { get; init; }

    public Guid? ItemId { get; init; }

    public IReadOnlyCollection<Guid>? ItemIds { get; init; }

    public Guid? PartId { get; init; }

    public IReadOnlyCollection<Guid>? PartIds { get; init; }

    public Guid? BatchId { get; init; }

    public Guid? LocationId { get; init; }
}

public interface IStockBalanceRepository
{
    /// <summary>Soldes existants pour ces cases (avec suivi des modifications).</summary>
    Task<IReadOnlyList<StockBalance>> GetForUpdateAsync(IReadOnlyCollection<StockKey> keys, CancellationToken cancellationToken = default);

    /// <summary>Soldes sans suivi, triés pour l'affichage.</summary>
    Task<IReadOnlyList<StockBalance>> ListAsync(BalanceFilter filter, CancellationToken cancellationToken = default);

    Task<bool> AnyAtLocationAsync(Guid locationId, CancellationToken cancellationToken = default);

    void Add(StockBalance balance);

    void Remove(StockBalance balance);
}

/// <summary>Unité de travail du stock : une par opération applicative, n'écrit que dans les tables <c>stk_</c>.</summary>
public interface IStockUnitOfWork : IUnitOfWork
{
    IStockLocationRepository Locations { get; }

    IStockItemRepository Items { get; }

    IStockTransactionRepository Transactions { get; }

    IStockBalanceRepository Balances { get; }

    ITrackedPartRepository TrackedParts { get; }
}
