namespace Barthazon.Catalog.Contracts;

/// <summary>Indice d'une pièce vu par les autres applications (lecture seule).</summary>
public sealed record PartRevisionSummary(
    Guid Id,
    Guid PartId,
    string Index,
    RevisionStatus Status,
    DateOnly? EffectiveDate,
    Guid? MaterialId,
    BlankShape? BlankShape,
    string? BlankDimensions);

/// <summary>Opération de gamme vue par les autres applications (lecture seule).</summary>
public sealed record RoutingOperationSummary(
    Guid Id,
    int Number,
    OperationType Type,
    Guid? MachineId,
    string? Description,
    decimal? SetupTimeMin,
    decimal? CycleTimeMin,
    string? Subcontractor);

/// <summary>Gamme d'un indice : opérations triées par numéro.</summary>
public sealed record RevisionRouting(PartRevisionSummary Revision, IReadOnlyList<RoutingOperationSummary> Operations);

/// <summary>Indices et gammes des pièces, en lecture seule (utilisé par le stock pour figer la gamme d'un lancement).</summary>
public interface IPartRoutingLookup
{
    /// <summary>Tous les indices de la pièce (annulés compris), du plus ancien au plus récent ; vide si la pièce est inconnue.</summary>
    Task<IReadOnlyList<PartRevisionSummary>> GetRevisionsAsync(Guid partId, CancellationToken cancellationToken = default);

    /// <summary>Gamme d'un indice, opérations triées par numéro ; null si l'indice est inconnu.</summary>
    Task<RevisionRouting?> GetRoutingAsync(Guid revisionId, CancellationToken cancellationToken = default);
}

/// <summary>Filtres de stock de la liste des pièces.</summary>
public enum PartStockFilter
{
    /// <summary>Pièces suivies sans aucune pièce finie en stock.</summary>
    OutOfStock,

    /// <summary>Pièces suivies sous leur seuil.</summary>
    BelowThreshold,
}

/// <summary>
/// Pièces correspondant à un filtre de stock. L'annuaire enregistre une implémentation vide par défaut ;
/// l'application Stock fournit la sienne.
/// </summary>
public interface IPartStockFilterProvider
{
    bool IsAvailable { get; }

    Task<IReadOnlySet<Guid>> GetPartIdsAsync(PartStockFilter filter, CancellationToken cancellationToken = default);
}

/// <summary>Implémentation par défaut : filtre non disponible (aucune pièce).</summary>
public sealed class UnavailablePartStockFilterProvider : IPartStockFilterProvider
{
    public bool IsAvailable => false;

    public Task<IReadOnlySet<Guid>> GetPartIdsAsync(PartStockFilter filter, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
}

/// <summary>Emplacements d'interface des pages de l'annuaire, où d'autres applications insèrent leurs composants.</summary>
public static class CatalogUiSlots
{
    /// <summary>Onglet « Stock » de la fiche pièce. Paramètre : PartId (Guid).</summary>
    public const string PartStockTab = "catalog.part.stock";

    /// <summary>Cartes supplémentaires du tableau de bord de l'annuaire. Aucun paramètre.</summary>
    public const string DashboardCards = "catalog.dashboard.cards";
}
