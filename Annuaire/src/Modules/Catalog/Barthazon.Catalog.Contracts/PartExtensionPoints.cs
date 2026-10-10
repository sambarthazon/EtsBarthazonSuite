namespace Barthazon.Catalog.Contracts;

/// <summary>Résumé de stock d'une pièce (fourni par la future application Stock).</summary>
public sealed record PartStockInfo(decimal Quantity, string? Unit, string? Location, decimal? MinimumThreshold, decimal? Value)
{
    public bool IsBelowThreshold => MinimumThreshold is { } threshold && Quantity < threshold;

    public bool IsOutOfStock => Quantity <= 0;
}

/// <summary>Livraison d'une pièce (future application Commandes &amp; livraisons).</summary>
public sealed record PartDeliveryInfo(DateOnly Date, decimal Quantity, string? DeliveryNoteNumber, string? CustomerName);

/// <summary>Commande en cours d'une pièce (future application Commandes &amp; livraisons).</summary>
public sealed record PartOrderInfo(string OrderNumber, DateOnly? DueDate, decimal OrderedQuantity, decimal RemainingQuantity, string? CustomerName);

/// <summary>
/// Stock d'une pièce. L'annuaire enregistre par défaut une implémentation vide (« non disponible ») ;
/// le module Stock enregistrera la sienne et les zones grisées s'activeront sans modifier l'annuaire.
/// </summary>
public interface IPartStockInfoProvider
{
    bool IsAvailable { get; }

    Task<PartStockInfo?> GetAsync(Guid partId, CancellationToken cancellationToken = default);

    Task<IReadOnlyDictionary<Guid, PartStockInfo>> GetManyAsync(IEnumerable<Guid> partIds, CancellationToken cancellationToken = default);
}

/// <summary>Livraisons d'une pièce (la plus récente en premier).</summary>
public interface IPartDeliveryInfoProvider
{
    bool IsAvailable { get; }

    Task<IReadOnlyList<PartDeliveryInfo>> GetHistoryAsync(Guid partId, int maxResults = 20, CancellationToken cancellationToken = default);
}

/// <summary>Commandes en cours d'une pièce.</summary>
public interface IPartOrderInfoProvider
{
    bool IsAvailable { get; }

    Task<IReadOnlyList<PartOrderInfo>> GetOpenOrdersAsync(Guid partId, CancellationToken cancellationToken = default);
}

/// <summary>Implémentations par défaut : données « non disponibles ».</summary>
public sealed class UnavailablePartInfoProvider : IPartStockInfoProvider, IPartDeliveryInfoProvider, IPartOrderInfoProvider
{
    public bool IsAvailable => false;

    public Task<PartStockInfo?> GetAsync(Guid partId, CancellationToken cancellationToken = default) => Task.FromResult<PartStockInfo?>(null);

    public Task<IReadOnlyDictionary<Guid, PartStockInfo>> GetManyAsync(IEnumerable<Guid> partIds, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, PartStockInfo>>(new Dictionary<Guid, PartStockInfo>());

    public Task<IReadOnlyList<PartDeliveryInfo>> GetHistoryAsync(Guid partId, int maxResults = 20, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PartDeliveryInfo>>([]);

    public Task<IReadOnlyList<PartOrderInfo>> GetOpenOrdersAsync(Guid partId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PartOrderInfo>>([]);
}
