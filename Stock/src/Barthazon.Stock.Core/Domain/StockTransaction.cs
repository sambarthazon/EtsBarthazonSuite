namespace Barthazon.Stock.Core.Domain;

/// <summary>
/// Mouvement : un fait (« op 20 terminée sur 28 pièces ») et les variations de stock qu'il entraîne (section 7.7).
/// Immuable : seule l'indication « annulé par » est renseignée après coup. Ce n'est pas une entité auditée : les
/// mouvements sont eux-mêmes un journal.
/// </summary>
public sealed class StockTransaction
{
    public const int ReferenceMaxLength = 100;
    public const int CommentMaxLength = 1000;
    public const int UserNameMaxLength = 100;

    private readonly List<StockTransactionLine> _lines = [];

    private StockTransaction()
    {
    }

    public StockTransaction(StockTransactionType type, DateTimeOffset occurredAt, DateTimeOffset recordedAt, Guid? recordedBy, string? recordedByName)
    {
        Id = Guid.NewGuid();
        Type = type;
        OccurredAt = occurredAt;
        RecordedAt = recordedAt;
        RecordedBy = recordedBy;
        RecordedByName = recordedByName;
    }

    public Guid Id { get; private set; }

    /// <summary>Numéro croissant, affiché « M-000123 ».</summary>
    public long Sequence { get; internal set; }

    public StockTransactionType Type { get; private set; }

    public DateTimeOffset OccurredAt { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    public Guid? RecordedBy { get; private set; }

    public string? RecordedByName { get; private set; }

    public Guid? BatchId { get; set; }

    /// <summary>Étape déclarée (déclaration d'opération).</summary>
    public int? StepSequence { get; set; }

    /// <summary>Bon de livraison, n° de commande…</summary>
    public string? Reference { get; set; }

    public StockReason? Reason { get; set; }

    public string? Comment { get; set; }

    public Guid? ReversesTransactionId { get; set; }

    /// <summary>Renseigné quand le mouvement a été annulé (seul champ modifiable après création).</summary>
    public Guid? ReversedByTransactionId { get; private set; }

    public IReadOnlyList<StockTransactionLine> Lines => _lines;

    public bool IsReversed => ReversedByTransactionId is not null;

    public string Number => FormatNumber(Sequence);

    public StockTransactionLine AddLine(StockKey key, decimal delta)
    {
        var line = new StockTransactionLine(Id, key, delta);
        _lines.Add(line);
        return line;
    }

    public void MarkReversedBy(Guid reversalId) => ReversedByTransactionId = reversalId;

    public static string FormatNumber(long sequence) => $"M-{sequence:000000}";
}

/// <summary>Variation d'une case de stock.</summary>
public sealed class StockTransactionLine
{
    private StockTransactionLine()
    {
    }

    internal StockTransactionLine(Guid transactionId, StockKey key, decimal delta)
    {
        Id = Guid.NewGuid();
        TransactionId = transactionId;
        SubjectKind = key.SubjectKind;
        ItemId = key.IsItem ? key.ItemId : null;
        PartId = key.IsItem ? null : key.PartId;
        RevisionId = key.IsItem ? null : key.RevisionId;
        Bucket = key.Bucket;
        BatchId = key.IsWorkInProgress ? key.BatchId : null;
        Stage = key.IsWorkInProgress ? key.Stage : null;
        LocationId = key.LocationId;
        Lot = key.Lot.Length == 0 ? null : key.Lot;
        Delta = delta;
    }

    public Guid Id { get; private set; }

    public Guid TransactionId { get; private set; }

    public StockSubjectKind SubjectKind { get; private set; }

    public Guid? ItemId { get; private set; }

    public Guid? PartId { get; private set; }

    public Guid? RevisionId { get; private set; }

    public StockBucket Bucket { get; private set; }

    public Guid? BatchId { get; private set; }

    public int? Stage { get; private set; }

    public Guid LocationId { get; private set; }

    public string? Lot { get; private set; }

    /// <summary>Variation signée (+ entrée, − sortie).</summary>
    public decimal Delta { get; private set; }

    public StockKey Key => new(SubjectKind, ItemId ?? Guid.Empty, PartId ?? Guid.Empty, RevisionId ?? Guid.Empty, Bucket,
        BatchId ?? Guid.Empty, Stage ?? StockKey.NoStage, LocationId, Lot ?? string.Empty);
}

/// <summary>Quantité actuelle d'une case de stock : cumul des lignes, tenu à jour dans la même transaction (section 7.8).</summary>
public sealed class StockBalance
{
    private StockBalance()
    {
    }

    public StockBalance(StockKey key)
    {
        Id = Guid.NewGuid();
        SubjectKind = key.SubjectKind;
        ItemId = key.ItemId;
        PartId = key.PartId;
        RevisionId = key.RevisionId;
        Bucket = key.Bucket;
        BatchId = key.BatchId;
        Stage = key.Stage;
        LocationId = key.LocationId;
        Lot = key.Lot;
    }

    public Guid Id { get; private set; }

    public StockSubjectKind SubjectKind { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid PartId { get; private set; }

    public Guid RevisionId { get; private set; }

    public StockBucket Bucket { get; private set; }

    public Guid BatchId { get; private set; }

    public int Stage { get; private set; }

    public Guid LocationId { get; private set; }

    public string Lot { get; private set; } = string.Empty;

    public decimal Quantity { get; private set; }

    public DateTimeOffset LastMovementAt { get; private set; }

    /// <summary>Jeton de concurrence : deux déclarations simultanées sur la même case sont sérialisées.</summary>
    public long Version { get; private set; }

    public StockKey Key => new(SubjectKind, ItemId, PartId, RevisionId, Bucket, BatchId, Stage, LocationId, Lot);

    public void Apply(decimal delta, DateTimeOffset at)
    {
        Quantity += delta;
        if (at > LastMovementAt)
        {
            LastMovementAt = at;
        }

        Version++;
    }

    /// <summary>Correction par l'outil « Vérifier les soldes ».</summary>
    public void Reset(decimal quantity)
    {
        Quantity = quantity;
        Version++;
    }
}
