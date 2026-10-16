using Barthazon.Catalog.Contracts;
using Barthazon.Stock.Core.Domain;

namespace Barthazon.Stock.Core.Services;

/// <summary>Emplacement (liste et édition).</summary>
public sealed record LocationItem(Guid Id, string Code, string Name, StockLocationKind Kind, string? Description, int SortOrder, bool IsArchived, long Version, bool HasStock);

/// <summary>Saisie d'un emplacement.</summary>
public sealed class LocationInput
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public StockLocationKind Kind { get; set; } = StockLocationKind.Storage;

    public string? Description { get; set; }

    public int SortOrder { get; set; }

    public long Version { get; set; }
}

/// <summary>Contenu d'un emplacement (page ouverte par son QR code).</summary>
public sealed record LocationDetails(LocationItem Location, IReadOnlyList<StockCellView> Contents);

/// <summary>
/// Une case de stock lisible : objet, emplacement, lot, quantité. <see cref="Key"/> identifie la case pour une saisie
/// (transfert, correction…).
/// </summary>
public sealed record StockCellView(
    StockKey Key,
    string SubjectCode,
    string SubjectLabel,
    StockUnit Unit,
    string LocationCode,
    string LocationName,
    decimal Quantity,
    DateTimeOffset LastMovementAt)
{
    public string? Lot => Key.Lot.Length == 0 ? null : Key.Lot;
}

/// <summary>Article dans une liste.</summary>
public sealed record ItemListItem(
    Guid Id,
    string Code,
    string Designation,
    StockItemKind Kind,
    Guid? MaterialId,
    string? MaterialName,
    string? Dimensions,
    StockUnit Unit,
    Guid? OwnerCustomerId,
    string? OwnerName,
    decimal Quantity,
    decimal? MinimumThreshold,
    decimal? Value,
    bool IsArchived)
{
    public bool IsBelowThreshold => MinimumThreshold is { } threshold && Quantity < threshold;
}

/// <summary>Filtre « propriétaire » de la liste des articles.</summary>
public enum ItemOwnerFilter
{
    All,
    Company,
    Customers,
}

/// <summary>Recherche d'articles (paramètres d'URL de la liste).</summary>
public sealed record ItemSearchRequest
{
    public string? Query { get; init; }

    public StockItemKind? Kind { get; init; }

    public Guid? MaterialId { get; init; }

    public ItemOwnerFilter Owner { get; init; }

    public Guid? OwnerCustomerId { get; init; }

    public bool BelowThresholdOnly { get; init; }

    public bool IncludeArchived { get; init; }
}

/// <summary>Fiche d'un article.</summary>
public sealed record ItemDetails(
    Guid Id,
    string Code,
    string Designation,
    StockItemKind Kind,
    Guid? MaterialId,
    string? MaterialName,
    BlankShape? Shape,
    string? Dimensions,
    StockUnit Unit,
    Guid? OwnerCustomerId,
    string? OwnerName,
    decimal? MinimumThreshold,
    Guid? DefaultLocationId,
    string? DefaultLocationCode,
    decimal? ReferenceUnitCost,
    string? UsualSupplier,
    bool TrackLots,
    string? Notes,
    bool IsArchived,
    long Version,
    DateTimeOffset UpdatedAt,
    string? UpdatedBy,
    decimal Quantity,
    decimal? Value,
    IReadOnlyList<StockCellView> Stock)
{
    public bool IsBelowThreshold => MinimumThreshold is { } threshold && Quantity < threshold;
}

/// <summary>Saisie d'un article.</summary>
public sealed class ItemInput
{
    public string Code { get; set; } = string.Empty;

    public string Designation { get; set; } = string.Empty;

    public StockItemKind Kind { get; set; } = StockItemKind.RawMaterial;

    public Guid? MaterialId { get; set; }

    public BlankShape? Shape { get; set; }

    public string? Dimensions { get; set; }

    public StockUnit Unit { get; set; } = StockUnit.Piece;

    public Guid? OwnerCustomerId { get; set; }

    public decimal? MinimumThreshold { get; set; }

    public Guid? DefaultLocationId { get; set; }

    public decimal? ReferenceUnitCost { get; set; }

    public string? UsualSupplier { get; set; }

    public bool TrackLots { get; set; }

    public string? Notes { get; set; }

    public long Version { get; set; }
}

/// <summary>Réception ou sortie d'un article (hors lancement).</summary>
public sealed class ItemMovementInput
{
    public Guid ItemId { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Vide : emplacement par défaut de l'article, sinon le magasin.</summary>
    public Guid? LocationId { get; set; }

    public string? Lot { get; set; }

    public string? Reference { get; set; }

    public string? Comment { get; set; }

    /// <summary>Moment du fait ; vide = maintenant.</summary>
    public DateTimeOffset? OccurredAt { get; set; }
}

/// <summary>Transfert d'une case de stock vers un autre emplacement.</summary>
public sealed class TransferInput
{
    public StockKey From { get; set; }

    public Guid ToLocationId { get; set; }

    public decimal Quantity { get; set; }

    public string? Comment { get; set; }

    public DateTimeOffset? OccurredAt { get; set; }
}

/// <summary>Correction d'une case de stock : la quantité constatée remplace la quantité connue.</summary>
public sealed class AdjustmentInput
{
    public StockKey Cell { get; set; }

    public decimal CountedQuantity { get; set; }

    public StockReason Reason { get; set; } = StockReason.Other;

    public string? Comment { get; set; }

    public DateTimeOffset? OccurredAt { get; set; }
}

/// <summary>Recherche dans le journal des mouvements (paramètres d'URL).</summary>
public sealed record MovementSearchRequest
{
    public DateOnly? From { get; init; }

    public DateOnly? To { get; init; }

    public StockTransactionType? Type { get; init; }

    public Guid? ItemId { get; init; }

    public Guid? PartId { get; init; }

    public Guid? BatchId { get; init; }

    public Guid? LocationId { get; init; }

    public string? UserName { get; init; }

    public bool IncludeReversed { get; init; } = true;

    public int PageIndex { get; init; }

    public int PageSize { get; init; } = 50;
}

/// <summary>Ligne de mouvement lisible.</summary>
public sealed record MovementLineView(
    StockKey Key,
    string SubjectCode,
    string SubjectLabel,
    StockUnit Unit,
    string LocationCode,
    string? StageLabel,
    decimal Delta)
{
    public string? Lot => Key.Lot.Length == 0 ? null : Key.Lot;
}

/// <summary>Mouvement dans le journal.</summary>
public sealed record MovementItem(
    Guid Id,
    string Number,
    StockTransactionType Type,
    DateTimeOffset OccurredAt,
    DateTimeOffset RecordedAt,
    string? RecordedByName,
    Guid? BatchId,
    string? Reference,
    StockReason? Reason,
    string? Comment,
    Guid? ReversesId,
    string? ReversesNumber,
    Guid? ReversedById,
    string? ReversedByNumber,
    IReadOnlyList<MovementLineView> Lines)
{
    public bool IsReversed => ReversedById is not null;

    public bool IsReversal => Type == StockTransactionType.Reversal;
}

/// <summary>Détail d'un mouvement et possibilité de l'annuler pour l'utilisateur courant.</summary>
public sealed record MovementDetails(MovementItem Movement, bool CanReverse, string? CannotReverseReason);

/// <summary>Écart entre un solde enregistré et la somme des lignes de mouvements.</summary>
public sealed record BalanceDiscrepancy(StockKey Key, decimal RecordedQuantity, decimal ComputedQuantity);
