using Barthazon.Catalog.Contracts;
using Barthazon.Shared.Kernel.Domain;
using Barthazon.Stock.Contracts;

namespace Barthazon.Stock.Core.Domain;

/// <summary>
/// Lancement : un lot de pièces identiques fabriquées ensemble, qui suit une gamme <b>figée</b> à sa création
/// (section 7.5). Les pièces en cours sont des cases de stock « en-cours » à une étape atteinte (section 8.1).
/// </summary>
public sealed class ProductionBatch : AuditableEntity
{
    public const int NumberMaxLength = 30;
    public const int ReferenceMaxLength = 50;
    public const int IndexMaxLength = 10;
    public const int OrderReferenceMaxLength = 100;
    public const int NotesMaxLength = 4000;

    private readonly List<BatchStep> _steps = [];

    private ProductionBatch()
    {
    }

    public ProductionBatch(string number, int year, int yearSequence, Guid partId, Guid revisionId, string partReference, string revisionIndex, int launchedQuantity, DateOnly launchedOn)
    {
        Number = number;
        Year = year;
        YearSequence = yearSequence;
        PartId = partId;
        RevisionId = revisionId;
        PartReference = partReference.Length <= ReferenceMaxLength ? partReference : partReference[..ReferenceMaxLength];
        RevisionIndex = revisionIndex;
        LaunchedQuantity = launchedQuantity;
        LaunchedOn = launchedOn;
        Status = BatchStatus.InProgress;
    }

    /// <summary>Numéro écrit sur la caisse et la fiche suiveuse (ex. L2026-014).</summary>
    public string Number { get; private set; } = string.Empty;

    /// <summary>Année et rang dans l'année (numérotation).</summary>
    public int Year { get; private set; }

    public int YearSequence { get; private set; }

    public Guid PartId { get; private set; }

    public Guid RevisionId { get; private set; }

    /// <summary>Instantanés au lancement.</summary>
    public string PartReference { get; private set; } = string.Empty;

    public string RevisionIndex { get; private set; } = string.Empty;

    public BatchStatus Status { get; private set; }

    public int LaunchedQuantity { get; private set; }

    public DateOnly LaunchedOn { get; private set; }

    public DateOnly? DueDate { get; set; }

    public string? CustomerOrderReference { get; set; }

    /// <summary>Lancement de reprise d'un en-cours existant (stock initial), créé à une étape donnée sans consommation.</summary>
    public bool IsRecovery { get; set; }

    public string? Notes { get; set; }

    public DateOnly? ClosedOn { get; private set; }

    /// <summary>Gamme figée, dans l'ordre des numéros d'opération.</summary>
    public IReadOnlyList<BatchStep> Steps => _steps.OrderBy(s => s.Sequence).ToList();

    public int StepCount => _steps.Count;

    public BatchStep? Step(int sequence) => _steps.FirstOrDefault(s => s.Sequence == sequence);

    /// <summary>Étape atteinte après l'opération de ce numéro (null si la gamme figée ne la contient pas).</summary>
    public int? StageAfterOperation(int operationNumber) => _steps.FirstOrDefault(s => s.OperationNumber == operationNumber)?.Sequence;

    /// <summary>Remplace la gamme figée (création ou resynchronisation).</summary>
    public void SetSteps(IEnumerable<BatchStepDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _steps.Clear();
        var sequence = 1;
        foreach (var definition in definitions.OrderBy(d => d.OperationNumber))
        {
            _steps.Add(new BatchStep(Id, sequence++, definition));
        }
    }

    /// <summary>Statut automatique : terminé quand plus aucune pièce n'est en cours, de nouveau en cours si une annulation en remet.</summary>
    public void UpdateStatus(decimal workInProgress, DateOnly today)
    {
        if (Status == BatchStatus.Cancelled)
        {
            return;
        }

        if (workInProgress <= 0 && Status == BatchStatus.InProgress)
        {
            Status = BatchStatus.Completed;
            ClosedOn = today;
        }
        else if (workInProgress > 0 && Status == BatchStatus.Completed)
        {
            Status = BatchStatus.InProgress;
            ClosedOn = null;
        }
    }

    public void Cancel(DateOnly today)
    {
        Status = BatchStatus.Cancelled;
        ClosedOn = today;
    }
}

/// <summary>Description d'une opération de gamme à figer dans un lancement.</summary>
public sealed record BatchStepDefinition(
    Guid? OperationId,
    int OperationNumber,
    OperationType OperationType,
    string? Description,
    Guid? MachineId,
    string? Subcontractor,
    decimal? SetupTimeMin,
    decimal? CycleTimeMin)
{
    /// <summary>Pièce sans gamme : une étape unique « Fabrication ».</summary>
    public static BatchStepDefinition Default(string description) => new(null, 10, OperationType.Other, description, null, null, null, null);

    public static BatchStepDefinition From(RoutingOperationSummary operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return new(operation.Id, operation.Number, operation.Type, operation.Description, operation.MachineId, operation.Subcontractor, operation.SetupTimeMin, operation.CycleTimeMin);
    }
}

/// <summary>Étape d'un lancement : copie d'une opération de la gamme au moment du lancement (section 7.6).</summary>
public sealed class BatchStep : Entity, IAuditChild
{
    public const int DescriptionMaxLength = 2000;
    public const int SubcontractorMaxLength = 200;

    private BatchStep()
    {
    }

    internal BatchStep(Guid batchId, int sequence, BatchStepDefinition definition)
    {
        BatchId = batchId;
        Sequence = sequence;
        OperationId = definition.OperationId;
        OperationNumber = definition.OperationNumber;
        OperationType = definition.OperationType;
        Description = definition.Description is { Length: > DescriptionMaxLength } d ? d[..DescriptionMaxLength] : definition.Description;
        MachineId = definition.MachineId;
        Subcontractor = definition.Subcontractor is { Length: > SubcontractorMaxLength } s ? s[..SubcontractorMaxLength] : definition.Subcontractor;
        SetupTimeMin = definition.SetupTimeMin;
        CycleTimeMin = definition.CycleTimeMin;
    }

    public Guid BatchId { get; private set; }

    /// <summary>1, 2, 3… dans l'ordre des numéros d'opération.</summary>
    public int Sequence { get; private set; }

    public Guid? OperationId { get; private set; }

    public int OperationNumber { get; private set; }

    public OperationType OperationType { get; private set; }

    public string? Description { get; private set; }

    public Guid? MachineId { get; private set; }

    public string? Subcontractor { get; private set; }

    public decimal? SetupTimeMin { get; private set; }

    public decimal? CycleTimeMin { get; private set; }

    public string AuditParentType => nameof(ProductionBatch);

    public Guid AuditParentId => BatchId;

    public BatchStepDefinition Definition => new(OperationId, OperationNumber, OperationType, Description, MachineId, Subcontractor, SetupTimeMin, CycleTimeMin);
}
