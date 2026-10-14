using Barthazon.Stock.Core.Domain;

namespace Barthazon.Stock.Core.Services;

/// <summary>Inventaire dans la liste.</summary>
public sealed record InventoryListItem(
    Guid Id,
    string Number,
    string Title,
    InventoryStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? ClosedAt,
    string? ClosedByName,
    string? CreatedBy,
    string Scope,
    bool IncludeWorkInProgress,
    int LineCount,
    int CountedCount,
    int DifferenceCount);

/// <summary>Ligne d'inventaire lisible.</summary>
public sealed record InventoryLineView(
    Guid Id,
    StockKey Key,
    string SubjectCode,
    string SubjectLabel,
    string? RevisionIndex,
    string? StageLabel,
    StockUnit Unit,
    decimal Expected,
    decimal? Counted,
    DateTimeOffset? CountedAt,
    string? CountedByName,
    bool IsUnexpected,
    bool MovedSinceStart)
{
    public string? Lot => Key.Lot.Length == 0 ? null : Key.Lot;

    public decimal? Difference => Counted - Expected;

    public bool HasDifference => Difference is { } d && d != 0;
}

/// <summary>Lignes d'un emplacement (la feuille de comptage est imprimée emplacement par emplacement).</summary>
public sealed record InventoryLocationGroup(Guid LocationId, string LocationCode, string LocationName, IReadOnlyList<InventoryLineView> Lines);

/// <summary>Inventaire et ses lignes, par emplacement.</summary>
public sealed record InventoryDetails(
    Guid Id,
    string Number,
    string Title,
    InventoryStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? ClosedAt,
    string? ClosedByName,
    string? CreatedBy,
    string Scope,
    bool IncludeWorkInProgress,
    IReadOnlyList<InventoryLocationGroup> Groups)
{
    public bool IsOpen => Status == InventoryStatus.Open;

    public IEnumerable<InventoryLineView> Lines => Groups.SelectMany(g => g.Lines);

    public int LineCount => Lines.Count();

    public int CountedCount => Lines.Count(l => l.Counted is not null);

    public int DifferenceCount => Lines.Count(l => l.HasDifference);

    public int MovedCount => Lines.Count(l => l.MovedSinceStart);
}

/// <summary>Ouverture d'un inventaire.</summary>
public sealed class OpenInventoryInput
{
    /// <summary>Vide = « Inventaire du JJ/MM/AAAA ».</summary>
    public string? Title { get; set; }

    /// <summary>Emplacements comptés ; vide = tout le stock.</summary>
    public List<Guid> LocationIds { get; set; } = [];

    public bool IncludeWorkInProgress { get; set; }
}

/// <summary>Quantité comptée d'une ligne (vide = pas encore comptée).</summary>
public sealed record InventoryCountInput(Guid LineId, decimal? Counted);

/// <summary>Ligne imprévue : un article ou une pièce finie trouvé à un emplacement.</summary>
public sealed class InventoryLineInput
{
    public Guid? ItemId { get; set; }

    public Guid? PartId { get; set; }

    /// <summary>Pièce finie : vide = indice en vigueur.</summary>
    public Guid? RevisionId { get; set; }

    public Guid LocationId { get; set; }

    public string? Lot { get; set; }

    public decimal Counted { get; set; }
}
