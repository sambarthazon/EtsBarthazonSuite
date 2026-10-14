using Barthazon.Catalog.Contracts;
using Barthazon.Stock.Contracts;
using Barthazon.Stock.Core.Domain;

namespace Barthazon.Stock.Core.Services;

/// <summary>Recherche de lancements (paramètres d'URL de la liste).</summary>
public sealed record BatchSearchRequest
{
    public string? Query { get; init; }

    /// <summary>Vide = tous les statuts.</summary>
    public BatchStatus? Status { get; init; }

    public Guid? PartId { get; init; }
}

/// <summary>Lancement dans une liste.</summary>
public sealed record BatchListItem(
    Guid Id,
    string Number,
    Guid PartId,
    string PartReference,
    string? PartDesignation,
    string RevisionIndex,
    bool RevisionIsInForce,
    BatchStatus Status,
    int LaunchedQuantity,
    int Finished,
    int Scrapped,
    int InProgress,
    int? CurrentOperationNumber,
    DateOnly LaunchedOn,
    DateOnly? DueDate,
    DateTimeOffset? LastMovementAt,
    bool IsStale,
    bool IsRecovery);

/// <summary>Étape d'un lancement : opération figée, pièces qui l'attendent, faites, rebutées.</summary>
public sealed record BatchStepView(
    int Sequence,
    int OperationNumber,
    OperationType OperationType,
    string? Description,
    Guid? MachineId,
    string? MachineName,
    string? Subcontractor,
    decimal? SetupTimeMin,
    decimal? CycleTimeMin,
    bool IsLast,
    int Waiting,
    int Done,
    int Skipped,
    int Scrapped,
    IReadOnlyList<StockCellView> WaitingCells,
    IReadOnlyList<SourceUsage> Variants);

/// <summary>Fiche d'un lancement.</summary>
public sealed record BatchDetails(
    Guid Id,
    string Number,
    Guid PartId,
    string PartReference,
    string? PartDesignation,
    Guid RevisionId,
    string RevisionIndex,
    RevisionStatus? RevisionStatus,
    BatchStatus Status,
    int LaunchedQuantity,
    DateOnly LaunchedOn,
    DateOnly? DueDate,
    string? CustomerOrderReference,
    bool IsRecovery,
    string? Notes,
    DateOnly? ClosedOn,
    long Version,
    int Finished,
    int Scrapped,
    int InProgress,
    IReadOnlyList<BatchStepView> Steps,
    IReadOnlyList<MovementLineView> Consumed)
{
    /// <summary>L'indice du lancement n'est plus (ou pas) en vigueur : bandeau d'avertissement.</summary>
    public bool RevisionIsInForce => RevisionStatus == Catalog.Contracts.RevisionStatus.InForce;
}

/// <summary>Case proposée pour la consommation d'un lancement (lot le plus ancien en premier).</summary>
public sealed record SourceCellOption(StockKey Key, string LocationCode, string? Lot, string? BatchNumber, decimal Quantity, StockUnit Unit);

/// <summary>Source utilisable pour un lancement, avec la quantité à consommer proposée et les cases disponibles.</summary>
public sealed record LaunchSourceOption(BlankSourceView Source, decimal ProposedQuantity, IReadOnlyList<SourceCellOption> Cells);

/// <summary>Aperçu d'un nouveau lancement : indice, gamme qui sera figée, sources.</summary>
public sealed record LaunchPreview(
    Guid PartId,
    string Reference,
    string Designation,
    IReadOnlyList<PartRevisionSummary> Revisions,
    PartRevisionSummary? Revision,
    IReadOnlyList<BatchStepDefinition> Steps,
    IReadOnlyList<LaunchSourceOption> Sources,
    string NextNumber);

/// <summary>Consommation d'une case de stock au lancement.</summary>
public sealed class ConsumptionInput
{
    public StockKey Cell { get; set; }

    public decimal Quantity { get; set; }
}

/// <summary>Nouveau lancement (section 10.5).</summary>
public sealed class LaunchInput
{
    public Guid PartId { get; set; }

    /// <summary>Vide = indice en vigueur.</summary>
    public Guid? RevisionId { get; set; }

    public int Quantity { get; set; }

    public DateOnly? DueDate { get; set; }

    public string? CustomerOrderReference { get; set; }

    public string? Notes { get; set; }

    /// <summary>Confirmation explicite pour lancer un indice qui n'est pas en vigueur.</summary>
    public bool ConfirmRevisionNotInForce { get; set; }

    /// <summary>Lancement sans consommation (matière non suivie), avec confirmation.</summary>
    public bool WithoutConsumption { get; set; }

    public Guid? SourceId { get; set; }

    public List<ConsumptionInput> Consumptions { get; set; } = [];

    /// <summary>Emplacement des pièces en cours ; vide = <c>Stock:DefaultWorkInProgressLocationCode</c>.</summary>
    public Guid? LocationId { get; set; }

    public DateTimeOffset? OccurredAt { get; set; }

    /// <summary>Lancement de reprise d'un en-cours existant : pas de consommation, pièces placées en attente de <see cref="RecoveryWaitingOperation"/>.</summary>
    public bool IsRecovery { get; set; }

    public int? RecoveryWaitingOperation { get; set; }
}

/// <summary>Déclaration d'une opération (bonnes, rebuts, non réalisée).</summary>
public sealed class DeclareInput
{
    public Guid BatchId { get; set; }

    public int StepSequence { get; set; }

    public int GoodQuantity { get; set; }

    public int ScrapQuantity { get; set; }

    public StockReason? ScrapReason { get; set; }

    /// <summary>Opération non réalisée (ex. ébavurage inutile) : les pièces passent quand même à l'étape suivante.</summary>
    public bool Skipped { get; set; }

    /// <summary>Dernière opération : rangement des pièces finies (vide = celui de la pièce suivie, sinon le magasin).</summary>
    public Guid? DestinationLocationId { get; set; }

    public string? Comment { get; set; }

    public DateTimeOffset? OccurredAt { get; set; }
}

/// <summary>Renvoi en reprise : des pièces en attente d'une étape repartent en attente d'une étape antérieure.</summary>
public sealed class ReworkInput
{
    public Guid BatchId { get; set; }

    /// <summary>Étape que les pièces attendent aujourd'hui (n° de séquence) ; la dernière + 1 n'existe pas.</summary>
    public int FromWaitingStep { get; set; }

    /// <summary>Étape à refaire (n° de séquence, antérieur).</summary>
    public int ToWaitingStep { get; set; }

    public int Quantity { get; set; }

    public string? Comment { get; set; }
}

/// <summary>Clôture d'un lancement : les pièces encore en cours sont déclarées en rebut.</summary>
public sealed class CloseBatchInput
{
    public Guid BatchId { get; set; }

    public StockReason Reason { get; set; } = StockReason.Other;

    public string? Comment { get; set; }
}

/// <summary>Échéance, référence de commande et notes d'un lancement.</summary>
public sealed class BatchEditInput
{
    public DateOnly? DueDate { get; set; }

    public string? CustomerOrderReference { get; set; }

    public string? Notes { get; set; }

    public long Version { get; set; }
}

/// <summary>Pipeline d'une pièce : en-cours par opération, lancement par lancement, et pièces finies (section 10.3).</summary>
public sealed record PartPipeline(IReadOnlyList<PipelineColumn> Columns, IReadOnlyList<PipelineRow> Rows, IReadOnlyDictionary<int, int> Totals, int TotalFinished);

public sealed record PipelineColumn(int OperationNumber, OperationType OperationType);

/// <summary>Ligne d'un lancement : pièces en attente de chaque opération (absente = opération hors de sa gamme).</summary>
public sealed record PipelineRow(Guid BatchId, string Number, string RevisionIndex, IReadOnlyDictionary<int, int> Waiting, IReadOnlyDictionary<int, int> StepByOperation, int Finished);
