using Barthazon.Shared.Kernel.Domain;

namespace Barthazon.Stock.Core.Domain;

/// <summary>État d'un inventaire.</summary>
public enum InventoryStatus
{
    Open,
    Validated,
    Cancelled,
}

/// <summary>
/// Inventaire : comptage physique d'une portée (emplacements, en-cours inclus ou non) et correction des écarts
/// (section 7.9). Les lignes sont les cases de stock attendues à l'ouverture, plus les objets trouvés là où on ne
/// les attendait pas.
/// </summary>
public sealed class InventoryCount : AuditableEntity
{
    public const int NumberMaxLength = 30;
    public const int TitleMaxLength = 200;

    private readonly List<InventoryCountLine> _lines = [];

    private InventoryCount()
    {
    }

    public InventoryCount(string number, int year, int yearSequence, string title, IReadOnlyCollection<Guid> locationIds, bool includeWorkInProgress, DateTimeOffset startedAt)
    {
        Number = number;
        Year = year;
        YearSequence = yearSequence;
        Title = title.Trim();
        LocationScope = string.Join(';', locationIds.Distinct());
        IncludeWorkInProgress = includeWorkInProgress;
        StartedAt = startedAt;
        Status = InventoryStatus.Open;
    }

    /// <summary>Numéro affiché (ex. INV2026-03).</summary>
    public string Number { get; private set; } = string.Empty;

    public int Year { get; private set; }

    public int YearSequence { get; private set; }

    public string Title { get; set; } = string.Empty;

    public InventoryStatus Status { get; private set; }

    /// <summary>Emplacements comptés (identifiants séparés par « ; ») ; vide = tout le stock.</summary>
    public string LocationScope { get; private set; } = string.Empty;

    /// <summary>Emplacements comptés ; vide = tout le stock.</summary>
    public IReadOnlyList<Guid> LocationIds =>
        LocationScope.Split(';', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList();

    public bool IncludeWorkInProgress { get; private set; }

    public DateTimeOffset StartedAt { get; private set; }

    public DateTimeOffset? ClosedAt { get; private set; }

    public string? ClosedByName { get; private set; }

    public IReadOnlyList<InventoryCountLine> Lines => _lines;

    public bool IsOpen => Status == InventoryStatus.Open;

    public bool Covers(Guid locationId) => LocationIds.Count == 0 || LocationIds.Contains(locationId);

    public InventoryCountLine AddLine(StockKey key, decimal expected, bool unexpected)
    {
        var line = new InventoryCountLine(Id, key, expected, unexpected);
        _lines.Add(line);
        return line;
    }

    public void RemoveLine(InventoryCountLine line) => _lines.Remove(line);

    public void Validate(DateTimeOffset at, string? by) => Close(InventoryStatus.Validated, at, by);

    public void Cancel(DateTimeOffset at, string? by) => Close(InventoryStatus.Cancelled, at, by);

    private void Close(InventoryStatus status, DateTimeOffset at, string? by)
    {
        Status = status;
        ClosedAt = at;
        ClosedByName = by;
    }
}

/// <summary>
/// Ligne d'inventaire : une case de stock, la quantité attendue à l'ouverture et la quantité comptée. Pas une entité
/// auditée (le comptage est tracé sur la ligne elle-même ; les corrections sont des mouvements).
/// </summary>
public sealed class InventoryCountLine
{
    private InventoryCountLine()
    {
    }

    internal InventoryCountLine(Guid inventoryId, StockKey key, decimal expected, bool unexpected)
    {
        Id = Guid.NewGuid();
        InventoryId = inventoryId;
        SubjectKind = key.SubjectKind;
        ItemId = key.ItemId;
        PartId = key.PartId;
        RevisionId = key.RevisionId;
        Bucket = key.Bucket;
        BatchId = key.BatchId;
        Stage = key.Stage;
        LocationId = key.LocationId;
        Lot = key.Lot;
        ExpectedQuantity = expected;
        IsUnexpected = unexpected;
    }

    public Guid Id { get; private set; }

    public Guid InventoryId { get; private set; }

    public StockSubjectKind SubjectKind { get; private set; }

    public Guid ItemId { get; private set; }

    public Guid PartId { get; private set; }

    public Guid RevisionId { get; private set; }

    public StockBucket Bucket { get; private set; }

    public Guid BatchId { get; private set; }

    public int Stage { get; private set; }

    public Guid LocationId { get; private set; }

    public string Lot { get; private set; } = string.Empty;

    /// <summary>Quantité connue à l'ouverture (ou à l'ajout de la ligne).</summary>
    public decimal ExpectedQuantity { get; private set; }

    /// <summary>Vide tant que la case n'est pas comptée.</summary>
    public decimal? CountedQuantity { get; private set; }

    public DateTimeOffset? CountedAt { get; private set; }

    public string? CountedByName { get; private set; }

    /// <summary>Objet trouvé là où il n'était pas attendu (ligne ajoutée pendant le comptage).</summary>
    public bool IsUnexpected { get; private set; }

    public StockKey Key => new(SubjectKind, ItemId, PartId, RevisionId, Bucket, BatchId, Stage, LocationId, Lot);

    public decimal? Difference => CountedQuantity - ExpectedQuantity;

    public void Count(decimal? quantity, DateTimeOffset at, string? by)
    {
        CountedQuantity = quantity;
        CountedAt = quantity is null ? null : at;
        CountedByName = quantity is null ? null : by;
    }
}
