using Barthazon.Catalog.Contracts;
using Barthazon.Shared.Kernel.Domain;

namespace Barthazon.Catalog.Core.Domain;

/// <summary>Version de pièce (indice), alignée sur l'indice du plan client.</summary>
public sealed class PartRevision : AuditableEntity, IAuditChild
{
    private readonly List<PartCharacteristic> _characteristics = [];
    private readonly List<RoutingOperation> _operations = [];
    private readonly List<PartDocument> _documents = [];

    private PartRevision()
    {
    }

    internal PartRevision(Guid partId, string index, int sequence, ChangeOrigin origin)
    {
        PartId = partId;
        Index = index.Trim();
        Sequence = sequence;
        ChangeOrigin = origin;
        Status = RevisionStatus.Draft;
    }

    public Guid PartId { get; private set; }

    /// <summary>Indice libre (A, B, 01, -…), unique par pièce.</summary>
    public string Index { get; internal set; } = string.Empty;

    /// <summary>Ordre chronologique, géré automatiquement.</summary>
    public int Sequence { get; private set; }

    public RevisionStatus Status { get; private set; }

    /// <summary>Renseignée à la mise en vigueur.</summary>
    public DateOnly? EffectiveDate { get; private set; }

    /// <summary>Ce qui change par rapport à l'indice précédent.</summary>
    public string? ChangeDescription { get; set; }

    public ChangeOrigin ChangeOrigin { get; set; }

    public Guid? MaterialId { get; set; }

    public string? MaterialNote { get; set; }

    public BlankShape? BlankShape { get; set; }

    /// <summary>Dimensions du brut / du débit (ex. Ø60 × 125).</summary>
    public string? BlankDimensions { get; set; }

    public decimal? LengthMm { get; set; }

    public decimal? WidthMm { get; set; }

    public decimal? HeightMm { get; set; }

    public decimal? DiameterMm { get; set; }

    public decimal? MassKg { get; set; }

    public string? GeneralTolerance { get; set; }

    public string? SurfaceRoughness { get; set; }

    public string? HeatTreatment { get; set; }

    public string? SurfaceTreatment { get; set; }

    public string? Hardness { get; set; }

    /// <summary>Outillages / montages spécifiques et leur rangement.</summary>
    public string? Tooling { get; set; }

    /// <summary>Consignes atelier, points d'attention.</summary>
    public string? WorkshopNotes { get; set; }

    /// <summary>Points de contrôle.</summary>
    public string? InspectionNotes { get; set; }

    public IReadOnlyList<PartCharacteristic> Characteristics => _characteristics;

    public IReadOnlyList<RoutingOperation> Operations => _operations;

    public IReadOnlyList<PartDocument> Documents => _documents;

    public string AuditParentType => nameof(Part);

    public Guid AuditParentId => PartId;

    internal void MarkInForce(DateOnly effectiveDate)
    {
        Status = RevisionStatus.InForce;
        EffectiveDate = effectiveDate;
    }

    internal void MarkSuperseded() => Status = RevisionStatus.Superseded;

    internal void MarkCancelled() => Status = RevisionStatus.Cancelled;

    public PartCharacteristic AddCharacteristic(string name, string? value, string? unit)
    {
        var characteristic = new PartCharacteristic(PartId, Id, name, value, unit, _characteristics.Count);
        _characteristics.Add(characteristic);
        return characteristic;
    }

    public void RemoveCharacteristic(PartCharacteristic characteristic) => _characteristics.Remove(characteristic);

    public RoutingOperation AddOperation(int number, OperationType type)
    {
        var operation = new RoutingOperation(PartId, Id, number, type);
        _operations.Add(operation);
        return operation;
    }

    public void RemoveOperation(RoutingOperation operation) => _operations.Remove(operation);

    public PartDocument AddDocument(DocumentKind kind, DocumentType type, string title)
    {
        var document = new PartDocument(PartId, Id, kind, type, title, _documents.Count == 0 ? 0 : _documents.Max(d => d.Order) + 1);
        _documents.Add(document);
        return document;
    }

    public void RemoveDocument(PartDocument document) => _documents.Remove(document);

    internal void AddDocumentCopy(PartDocument source)
    {
        var copy = AddDocument(source.Kind, source.Type, source.Title);
        copy.CopyFileFrom(source);
    }

    internal void CopySpecificationsFrom(PartRevision source)
    {
        MaterialId = source.MaterialId;
        MaterialNote = source.MaterialNote;
        BlankShape = source.BlankShape;
        BlankDimensions = source.BlankDimensions;
        LengthMm = source.LengthMm;
        WidthMm = source.WidthMm;
        HeightMm = source.HeightMm;
        DiameterMm = source.DiameterMm;
        MassKg = source.MassKg;
        GeneralTolerance = source.GeneralTolerance;
        SurfaceRoughness = source.SurfaceRoughness;
        HeatTreatment = source.HeatTreatment;
        SurfaceTreatment = source.SurfaceTreatment;
        Hardness = source.Hardness;
        Tooling = source.Tooling;
        WorkshopNotes = source.WorkshopNotes;
        InspectionNotes = source.InspectionNotes;
        foreach (var c in source.Characteristics.OrderBy(c => c.Order))
        {
            AddCharacteristic(c.Name, c.Value, c.Unit);
        }

        foreach (var o in source.Operations.OrderBy(o => o.Number))
        {
            var operation = AddOperation(o.Number, o.Type);
            operation.CopyFrom(o);
        }
    }
}

/// <summary>Caractéristique libre (ex. « Filetage : M12×1,75 »).</summary>
public sealed class PartCharacteristic : Entity, IAuditChild
{
    private PartCharacteristic()
    {
    }

    internal PartCharacteristic(Guid partId, Guid revisionId, string name, string? value, string? unit, int order)
    {
        PartId = partId;
        RevisionId = revisionId;
        Name = name.Trim();
        Value = value?.Trim();
        Unit = unit?.Trim();
        Order = order;
    }

    public Guid PartId { get; private set; }

    public Guid RevisionId { get; private set; }

    public string Name { get; set; } = string.Empty;

    public string? Value { get; set; }

    public string? Unit { get; set; }

    public int Order { get; set; }

    public string AuditParentType => nameof(Part);

    public Guid AuditParentId => PartId;
}

/// <summary>Opération de gamme.</summary>
public sealed class RoutingOperation : Entity, IAuditChild
{
    private RoutingOperation()
    {
    }

    internal RoutingOperation(Guid partId, Guid revisionId, int number, OperationType type)
    {
        PartId = partId;
        RevisionId = revisionId;
        Number = number;
        Type = type;
    }

    public Guid PartId { get; private set; }

    public Guid RevisionId { get; private set; }

    /// <summary>10, 20, 30…</summary>
    public int Number { get; set; }

    public OperationType Type { get; set; }

    public Guid? MachineId { get; set; }

    public string? Description { get; set; }

    /// <summary>Temps de réglage (min).</summary>
    public decimal? SetupTimeMin { get; set; }

    /// <summary>Temps unitaire (min).</summary>
    public decimal? CycleTimeMin { get; set; }

    public string? NcProgramReference { get; set; }

    public string? Tooling { get; set; }

    public string? Subcontractor { get; set; }

    public string AuditParentType => nameof(Part);

    public Guid AuditParentId => PartId;

    internal void CopyFrom(RoutingOperation source)
    {
        MachineId = source.MachineId;
        Description = source.Description;
        SetupTimeMin = source.SetupTimeMin;
        CycleTimeMin = source.CycleTimeMin;
        NcProgramReference = source.NcProgramReference;
        Tooling = source.Tooling;
        Subcontractor = source.Subcontractor;
    }
}

/// <summary>Document attaché à un indice : fichier numérique ou référence à un document papier.</summary>
public sealed class PartDocument : AuditableEntity, IAuditChild
{
    private PartDocument()
    {
    }

    internal PartDocument(Guid partId, Guid revisionId, DocumentKind kind, DocumentType type, string title, int order)
    {
        PartId = partId;
        RevisionId = revisionId;
        Kind = kind;
        Type = type;
        Title = title.Trim();
        Order = order;
    }

    public Guid PartId { get; private set; }

    public Guid RevisionId { get; private set; }

    public DocumentKind Kind { get; set; }

    public DocumentType Type { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? FileName { get; private set; }

    public string? ContentType { get; private set; }

    public long? SizeBytes { get; private set; }

    public string? Sha256 { get; private set; }

    /// <summary>Clé dans le stockage de fichiers (jamais de binaire en base).</summary>
    public string? StorageKey { get; private set; }

    /// <summary>Document papier : où trouver l'original.</summary>
    public string? PaperLocation { get; set; }

    public string? Notes { get; set; }

    public int Order { get; set; }

    public string AuditParentType => nameof(Part);

    public Guid AuditParentId => PartId;

    public void AttachFile(string fileName, string contentType, long sizeBytes, string sha256, string storageKey)
    {
        Kind = DocumentKind.File;
        FileName = fileName;
        ContentType = contentType;
        SizeBytes = sizeBytes;
        Sha256 = sha256;
        StorageKey = storageKey;
    }

    internal void CopyFileFrom(PartDocument source)
    {
        FileName = source.FileName;
        ContentType = source.ContentType;
        SizeBytes = source.SizeBytes;
        Sha256 = source.Sha256;
        StorageKey = source.StorageKey;
        PaperLocation = source.PaperLocation;
        Notes = source.Notes;
    }
}
