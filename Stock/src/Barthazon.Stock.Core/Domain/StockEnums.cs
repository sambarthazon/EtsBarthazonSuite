namespace Barthazon.Stock.Core.Domain;

/// <summary>Type d'article : tout ce qui se stocke et n'est pas une pièce de l'annuaire.</summary>
public enum StockItemKind
{
    /// <summary>Matière : barre, tube, tôle, plat…</summary>
    RawMaterial,

    /// <summary>Brut : débit, pièce moulée / forgée, ébauche.</summary>
    Blank,

    Other,
}

/// <summary>Unité de stock d'un article.</summary>
public enum StockUnit
{
    Piece,
    Meter,
    Kilogram,
    SquareMeter,
}

/// <summary>Objet d'une case de stock : un article ou une pièce de l'annuaire.</summary>
public enum StockSubjectKind
{
    Item,
    Part,
}

/// <summary>Nature d'une case : en magasin (article, pièce finie) ou en cours dans un lancement.</summary>
public enum StockBucket
{
    Stock,
    WorkInProgress,
}

/// <summary>Types de mouvements (section 8.3).</summary>
public enum StockTransactionType
{
    ItemReceipt,
    ItemIssue,
    Launch,
    OperationDone,
    OperationSkipped,
    Scrap,
    Rework,
    Transfer,
    Shipment,
    PartReceipt,
    Adjustment,
    Reversal,
}

/// <summary>Motif d'un rebut ou d'une correction.</summary>
public enum StockReason
{
    Dimension,
    Aspect,
    Material,
    Setup,
    Handling,
    Inventory,
    Other,

    /// <summary>Reprise du stock existant (import, première saisie).</summary>
    InitialStock,
}
