using Barthazon.Catalog.Contracts;
using Barthazon.Stock.Core.Domain;

namespace Barthazon.Stock.Core.Services;

/// <summary>Filtre « état » de la liste des pièces suivies.</summary>
public enum TrackedPartStateFilter
{
    All,
    Ok,
    BelowThreshold,
    OutOfStock,
    SupersededRevision,
}

/// <summary>Recherche de pièces suivies (paramètres d'URL de la liste).</summary>
public sealed record TrackedPartSearchRequest
{
    public string? Query { get; init; }

    public Guid? CustomerId { get; init; }

    public TrackedPartStateFilter State { get; init; }

    /// <summary>Seulement les pièces qui ont des pièces en cours.</summary>
    public bool WithWorkInProgressOnly { get; init; }

    /// <summary>Seulement les pièces qui ont du stock (fini ou en cours) à cet emplacement.</summary>
    public Guid? LocationId { get; init; }
}

/// <summary>Pièce suivie dans une liste.</summary>
public sealed record TrackedPartListItem(
    Guid PartId,
    string Reference,
    string Designation,
    Guid? CustomerId,
    string? CustomerName,
    bool IsUnknownPart,
    PartStatus? Status,
    bool IsArchived,
    string? InForceIndex,
    int Finished,
    int WorkInProgress,
    int? MinimumThreshold,
    bool HasSupersededStock)
{
    public bool IsOutOfStock => Finished <= 0;

    public bool IsBelowThreshold => MinimumThreshold is { } threshold && Finished < threshold;

    /// <summary>Sous le seuil mais couverte par les en-cours (section 8.6) : pas d'alerte, une simple mention.</summary>
    public bool IsCoveredByWorkInProgress => IsBelowThreshold && Finished + WorkInProgress >= MinimumThreshold;
}

/// <summary>Source de brut lisible, avec ce que son stock permet de lancer.</summary>
public sealed record BlankSourceView(
    Guid Id,
    BlankSourceKind Kind,
    Guid? ItemId,
    Guid? SourcePartId,
    string SourceCode,
    string SourceLabel,
    StockUnit Unit,
    int? SourceAfterOperation,
    decimal QuantityPerPart,
    Guid? RevisionId,
    string? RevisionIndex,
    bool IsDefault,
    int SortOrder,
    string? Note,
    decimal Available,
    int Launchable);

/// <summary>Rappel de l'annuaire pour choisir la première source : matière, forme et dimensions du brut de l'indice en vigueur.</summary>
public sealed record BlankHint(Guid? MaterialId, string? MaterialName, BlankShape? Shape, string? Dimensions, Guid? SuggestedItemId, string? SuggestedItemLabel);

/// <summary>Fiche stock d'une pièce (suivie ou non).</summary>
public sealed record TrackedPartDetails(
    Guid PartId,
    string Reference,
    string Designation,
    Guid? CustomerId,
    string? CustomerName,
    bool IsUnknownPart,
    PartStatus? Status,
    bool IsArchived,
    bool IsTracked,
    PartRevisionSummary? InForceRevision,
    IReadOnlyList<PartRevisionSummary> Revisions,
    int? MinimumThreshold,
    Guid? DefaultLocationId,
    string? DefaultLocationCode,
    string? Notes,
    long Version,
    int Finished,
    int WorkInProgress,
    IReadOnlyList<StockCellView> FinishedStock,
    IReadOnlyList<BlankSourceView> Sources,
    BlankHint Hint)
{
    public bool IsBelowThreshold => MinimumThreshold is { } threshold && Finished < threshold;
}

/// <summary>Paramètres de stock d'une pièce suivie.</summary>
public sealed class TrackedPartSettingsInput
{
    public int? MinimumThreshold { get; set; }

    public Guid? DefaultLocationId { get; set; }

    public string? Notes { get; set; }

    public long Version { get; set; }
}

/// <summary>Saisie d'une source de brut.</summary>
public sealed class BlankSourceInput
{
    public BlankSourceKind Kind { get; set; } = BlankSourceKind.Item;

    public Guid? ItemId { get; set; }

    public Guid? SourcePartId { get; set; }

    /// <summary>Vide = pièce source finie ; sinon « après l'opération n° ».</summary>
    public int? SourceAfterOperation { get; set; }

    public decimal QuantityPerPart { get; set; } = 1;

    /// <summary>Vide = valable pour tous les indices.</summary>
    public Guid? RevisionId { get; set; }

    public bool IsDefault { get; set; }

    public string? Note { get; set; }
}

/// <summary>Pièce qui utilise un article ou une autre pièce comme source (« pièces issues de ce brut »).</summary>
public sealed record SourceUsage(Guid PartId, string Reference, string Designation, decimal QuantityPerPart, int? SourceAfterOperation, string? RevisionIndex, int Launchable);
