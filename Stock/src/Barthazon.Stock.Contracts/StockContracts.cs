using Barthazon.Catalog.Contracts;
using Barthazon.Shared.Kernel.Results;

namespace Barthazon.Stock.Contracts;

/// <summary>Case de pièces finies : indice, emplacement, lot (numéro du lancement d'origine) et quantité.</summary>
public sealed record FinishedStockLine(
    Guid PartId,
    Guid RevisionId,
    string RevisionIndex,
    bool RevisionIsInForce,
    string LocationCode,
    string? Lot,
    int Quantity);

/// <summary>Pièces en cours d'une pièce, tous lancements confondus.</summary>
public sealed record WorkInProgressSummary(Guid PartId, int Quantity, int BatchCount);

/// <summary>Lecture du stock de pièces (future application « Commandes &amp; livraisons »).</summary>
public interface IStockQuery
{
    /// <summary>Pièces finies disponibles, indice en vigueur d'abord.</summary>
    Task<IReadOnlyList<FinishedStockLine>> GetFinishedStockAsync(Guid partId, CancellationToken cancellationToken = default);

    Task<WorkInProgressSummary> GetWorkInProgressAsync(Guid partId, CancellationToken cancellationToken = default);
}

/// <summary>Étape figée d'un lancement.</summary>
public sealed record BatchStepSummary(Guid StepId, int Sequence, int OperationNumber, OperationType Type, Guid? MachineId, string? Description);

/// <summary>Lancement et ses étapes.</summary>
public sealed record BatchSummary(
    Guid Id,
    string Number,
    Guid PartId,
    Guid RevisionId,
    BatchStatus Status,
    int LaunchedQuantity,
    IReadOnlyList<BatchStepSummary> Steps);

/// <summary>Lancements et étapes (future application « Pointage »).</summary>
public interface IBatchLookup
{
    Task<BatchSummary?> GetAsync(Guid batchId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<BatchSummary>> ListOpenAsync(CancellationToken cancellationToken = default);
}

/// <summary>Demande de sortie de pièces finies (livraison).</summary>
/// <param name="RevisionId">Vide : indice en vigueur d'abord, puis les autres.</param>
/// <param name="LocationCode">Vide : tous les emplacements.</param>
/// <param name="Lot">Vide : lot le plus ancien d'abord.</param>
/// <param name="Reference">N° de bon de livraison…</param>
/// <param name="OccurredAt">Vide : maintenant.</param>
/// <param name="ConfirmSupersededRevision">Confirme la sortie de pièces à un indice qui n'est plus en vigueur.</param>
public sealed record ShipmentRequest(
    Guid PartId,
    Guid? RevisionId,
    int Quantity,
    string? LocationCode,
    string? Lot,
    string? Reference,
    DateTimeOffset? OccurredAt,
    bool ConfirmSupersededRevision);

/// <summary>Sorties de pièces finies (future application « Commandes &amp; livraisons »).</summary>
public interface IStockShipments
{
    /// <summary>Sortie de pièces finies (livraison). Mêmes règles et droits que la saisie manuelle. Renvoie l'identifiant du mouvement.</summary>
    Task<Result<Guid>> RecordShipmentAsync(ShipmentRequest request, CancellationToken cancellationToken = default);
}
