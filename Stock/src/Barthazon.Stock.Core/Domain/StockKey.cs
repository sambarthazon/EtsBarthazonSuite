namespace Barthazon.Stock.Core.Domain;

/// <summary>
/// Clé d'une case de stock (section 7.8). Aucune composante n'est nulle : valeurs neutres (<see cref="Guid.Empty"/>,
/// −1, chaîne vide) quand la notion ne s'applique pas, pour que l'index unique se comporte pareil sous SQLite et PostgreSQL.
/// </summary>
public readonly record struct StockKey(
    StockSubjectKind SubjectKind,
    Guid ItemId,
    Guid PartId,
    Guid RevisionId,
    StockBucket Bucket,
    Guid BatchId,
    int Stage,
    Guid LocationId,
    string Lot)
{
    public const int NoStage = -1;
    public const int LotMaxLength = 50;

    /// <summary>Article en magasin.</summary>
    public static StockKey ForItem(Guid itemId, Guid locationId, string? lot) =>
        new(StockSubjectKind.Item, itemId, Guid.Empty, Guid.Empty, StockBucket.Stock, Guid.Empty, NoStage, locationId, NormalizeLot(lot));

    /// <summary>Pièce finie en magasin (le lot est le numéro du lancement d'origine).</summary>
    public static StockKey ForFinishedPart(Guid partId, Guid revisionId, Guid locationId, string? lot) =>
        new(StockSubjectKind.Part, Guid.Empty, partId, revisionId, StockBucket.Stock, Guid.Empty, NoStage, locationId, NormalizeLot(lot));

    /// <summary>Pièce en cours dans un lancement, à l'étape atteinte <paramref name="stage"/>.</summary>
    public static StockKey ForWorkInProgress(Guid partId, Guid revisionId, Guid batchId, int stage, Guid locationId) =>
        new(StockSubjectKind.Part, Guid.Empty, partId, revisionId, StockBucket.WorkInProgress, batchId, stage, locationId, string.Empty);

    public bool IsItem => SubjectKind == StockSubjectKind.Item;

    public bool IsWorkInProgress => Bucket == StockBucket.WorkInProgress;

    public bool IsFinishedPart => SubjectKind == StockSubjectKind.Part && Bucket == StockBucket.Stock;

    /// <summary>Même case à un autre emplacement.</summary>
    public StockKey At(Guid locationId) => this with { LocationId = locationId };

    public static string NormalizeLot(string? lot) => lot?.Trim() ?? string.Empty;
}
