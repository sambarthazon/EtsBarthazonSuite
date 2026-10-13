using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Persistence;

namespace Barthazon.Catalog.Core.Services;

/// <summary>Tri de la liste des pièces.</summary>
public enum PartSort
{
    Reference,
    Designation,
    Customer,
    CustomerReference,
    Index,
    Material,
    Status,
    UpdatedAt,
}

/// <summary>Recherche et filtres de la liste des pièces (reflétés dans l'URL).</summary>
public sealed record PartSearchRequest
{
    public string? Query { get; init; }

    public Guid? CustomerId { get; init; }

    public Guid? FamilyId { get; init; }

    public Guid? MaterialId { get; init; }

    public IReadOnlyList<PartStatus> Statuses { get; init; } = [];

    public IReadOnlyList<Guid> TagIds { get; init; } = [];

    public Guid? MachineId { get; init; }

    public bool? HasDrawing { get; init; }

    public bool IncludeArchived { get; init; }

    public PartQualityIssue Quality { get; init; }

    public PartSort Sort { get; init; } = PartSort.Reference;

    public bool Descending { get; init; }

    public int PageIndex { get; init; }

    public int PageSize { get; init; } = 50;
}

/// <summary>Étiquette affichée sur une pièce.</summary>
public sealed record TagLabel(Guid Id, string Label, TagColor Color);

/// <summary>Ligne de la liste des pièces.</summary>
public sealed record PartListItem(
    Guid Id,
    string InternalReference,
    string Designation,
    Guid CustomerId,
    string CustomerCode,
    string CustomerName,
    string? CustomerReference,
    string? FamilyName,
    PartStatus Status,
    string? CurrentIndex,
    RevisionStatus? CurrentRevisionStatus,
    string? MaterialDisplay,
    DateTimeOffset UpdatedAt,
    Guid? ThumbnailDocumentId,
    bool IsArchived,
    IReadOnlyList<TagLabel> Tags);

/// <summary>Résumé d'un indice (chronologie).</summary>
public sealed record RevisionSummary(
    Guid Id,
    string Index,
    int Sequence,
    RevisionStatus Status,
    DateOnly? EffectiveDate,
    string? ChangeDescription,
    ChangeOrigin ChangeOrigin,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    int DocumentCount);

/// <summary>Fiche pièce (en-tête et indices).</summary>
public sealed record PartDetails(
    Guid Id,
    string InternalReference,
    string Designation,
    Guid CustomerId,
    string CustomerCode,
    string CustomerName,
    string? CustomerReference,
    Guid? FamilyId,
    string? FamilyName,
    PartStatus Status,
    string? Description,
    IReadOnlyList<TagLabel> Tags,
    Guid? MainPhotoDocumentId,
    bool IsArchived,
    long Version,
    DateTimeOffset CreatedAt,
    string? CreatedBy,
    DateTimeOffset UpdatedAt,
    string? UpdatedBy,
    Guid? CurrentRevisionId,
    Guid? InForceRevisionId,
    IReadOnlyList<RevisionSummary> Revisions)
{
    public RevisionSummary? InForceRevision => Revisions.FirstOrDefault(r => r.Id == InForceRevisionId);

    public RevisionSummary? FindRevision(string? index) =>
        index is null ? null : Revisions.FirstOrDefault(r => string.Equals(r.Index, index, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Caractéristique libre.</summary>
public sealed record CharacteristicDto(Guid Id, string Name, string? Value, string? Unit);

/// <summary>Opération de gamme.</summary>
public sealed record OperationDto(
    Guid Id,
    int Number,
    OperationType Type,
    Guid? MachineId,
    string? MachineName,
    string? Description,
    decimal? SetupTimeMin,
    decimal? CycleTimeMin,
    string? NcProgramReference,
    string? Tooling,
    string? Subcontractor);

/// <summary>Document d'un indice.</summary>
public sealed record DocumentDto(
    Guid Id,
    DocumentKind Kind,
    DocumentType Type,
    string Title,
    string? FileName,
    string? ContentType,
    long? SizeBytes,
    string? PaperLocation,
    string? Notes,
    DateTimeOffset CreatedAt,
    string? CreatedBy)
{
    /// <summary>Aperçu intégré possible (PDF ou image sûre).</summary>
    public bool CanPreview => Kind == DocumentKind.File && DocumentContentTypes.IsPreviewable(ContentType);

    public bool IsImage => Kind == DocumentKind.File && DocumentContentTypes.IsImage(ContentType);

    public bool IsPdf => Kind == DocumentKind.File && DocumentContentTypes.IsPdf(ContentType);
}

/// <summary>Indice complet (spécifications, caractéristiques, gamme, documents).</summary>
public sealed record RevisionDetails(
    Guid Id,
    Guid PartId,
    string Index,
    int Sequence,
    RevisionStatus Status,
    DateOnly? EffectiveDate,
    string? ChangeDescription,
    ChangeOrigin ChangeOrigin,
    Guid? MaterialId,
    string? MaterialDisplay,
    string? MaterialNote,
    BlankShape? BlankShape,
    string? BlankDimensions,
    decimal? LengthMm,
    decimal? WidthMm,
    decimal? HeightMm,
    decimal? DiameterMm,
    decimal? MassKg,
    string? GeneralTolerance,
    string? SurfaceRoughness,
    string? HeatTreatment,
    string? SurfaceTreatment,
    string? Hardness,
    string? Tooling,
    string? WorkshopNotes,
    string? InspectionNotes,
    IReadOnlyList<CharacteristicDto> Characteristics,
    IReadOnlyList<OperationDto> Operations,
    IReadOnlyList<DocumentDto> Documents,
    long Version,
    DateTimeOffset UpdatedAt,
    string? UpdatedBy)
{
    public decimal TotalSetupTimeMin => Operations.Sum(o => o.SetupTimeMin ?? 0);

    public decimal TotalCycleTimeMin => Operations.Sum(o => o.CycleTimeMin ?? 0);

    /// <summary>Plan principal : premier plan affichable (PDF, image), sinon plan papier, sinon autre plan numérique.</summary>
    public DocumentDto? MainDrawing =>
        Documents.FirstOrDefault(d => d.Type == DocumentType.Drawing && d.CanPreview)
        ?? Documents.FirstOrDefault(d => d.Type == DocumentType.Drawing && d.Kind == DocumentKind.Paper)
        ?? Documents.FirstOrDefault(d => d.Type == DocumentType.Drawing);
}

/// <summary>Saisie de l'en-tête d'une pièce.</summary>
public class PartInput
{
    public string InternalReference { get; set; } = string.Empty;

    public string Designation { get; set; } = string.Empty;

    public Guid? CustomerId { get; set; }

    public string? CustomerReference { get; set; }

    public Guid? FamilyId { get; set; }

    public PartStatus Status { get; set; } = PartStatus.Active;

    public string? Description { get; set; }

    public List<Guid> TagIds { get; set; } = [];

    public long Version { get; set; }

    public static PartInput From(PartDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        return new PartInput
        {
            InternalReference = details.InternalReference,
            Designation = details.Designation,
            CustomerId = details.CustomerId,
            CustomerReference = details.CustomerReference,
            FamilyId = details.FamilyId,
            Status = details.Status,
            Description = details.Description,
            TagIds = details.Tags.Select(t => t.Id).ToList(),
            Version = details.Version,
        };
    }
}

/// <summary>Création d'une pièce avec son premier indice.</summary>
public sealed class PartCreateInput : PartInput
{
    public string FirstIndex { get; set; } = "A";

    /// <summary>Premier indice directement en vigueur (sinon brouillon).</summary>
    public bool PutInForce { get; set; } = true;

    public Guid? MaterialId { get; set; }

    public string? MaterialNote { get; set; }

    public BlankShape? BlankShape { get; set; }

    public string? BlankDimensions { get; set; }

    public string? ChangeDescription { get; set; }

    /// <summary>Duplication : pièce dont on reprend les spécifications, caractéristiques et gamme.</summary>
    public Guid? DuplicateFromPartId { get; set; }

    public Guid? DuplicateFromRevisionId { get; set; }
}

/// <summary>Saisie d'une caractéristique.</summary>
public sealed class CharacteristicInput
{
    public Guid? Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Value { get; set; }

    public string? Unit { get; set; }
}

/// <summary>Saisie d'une opération de gamme.</summary>
public sealed class OperationInput
{
    public Guid? Id { get; set; }

    public int Number { get; set; }

    public OperationType Type { get; set; } = OperationType.Turning;

    public Guid? MachineId { get; set; }

    public string? Description { get; set; }

    public decimal? SetupTimeMin { get; set; }

    public decimal? CycleTimeMin { get; set; }

    public string? NcProgramReference { get; set; }

    public string? Tooling { get; set; }

    public string? Subcontractor { get; set; }
}

/// <summary>Saisie d'un indice (spécifications, caractéristiques, gamme).</summary>
public sealed class RevisionInput
{
    public string Index { get; set; } = string.Empty;

    public string? ChangeDescription { get; set; }

    public ChangeOrigin ChangeOrigin { get; set; } = ChangeOrigin.CustomerRequest;

    public Guid? MaterialId { get; set; }

    public string? MaterialNote { get; set; }

    public BlankShape? BlankShape { get; set; }

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

    public string? Tooling { get; set; }

    public string? WorkshopNotes { get; set; }

    public string? InspectionNotes { get; set; }

    public List<CharacteristicInput> Characteristics { get; set; } = [];

    public List<OperationInput> Operations { get; set; } = [];

    public long Version { get; set; }

    public static RevisionInput From(RevisionDetails r)
    {
        ArgumentNullException.ThrowIfNull(r);
        return new RevisionInput
        {
            Index = r.Index,
            ChangeDescription = r.ChangeDescription,
            ChangeOrigin = r.ChangeOrigin,
            MaterialId = r.MaterialId,
            MaterialNote = r.MaterialNote,
            BlankShape = r.BlankShape,
            BlankDimensions = r.BlankDimensions,
            LengthMm = r.LengthMm,
            WidthMm = r.WidthMm,
            HeightMm = r.HeightMm,
            DiameterMm = r.DiameterMm,
            MassKg = r.MassKg,
            GeneralTolerance = r.GeneralTolerance,
            SurfaceRoughness = r.SurfaceRoughness,
            HeatTreatment = r.HeatTreatment,
            SurfaceTreatment = r.SurfaceTreatment,
            Hardness = r.Hardness,
            Tooling = r.Tooling,
            WorkshopNotes = r.WorkshopNotes,
            InspectionNotes = r.InspectionNotes,
            Characteristics = r.Characteristics.Select(c => new CharacteristicInput { Id = c.Id, Name = c.Name, Value = c.Value, Unit = c.Unit }).ToList(),
            Operations = r.Operations.Select(o => new OperationInput
            {
                Id = o.Id,
                Number = o.Number,
                Type = o.Type,
                MachineId = o.MachineId,
                Description = o.Description,
                SetupTimeMin = o.SetupTimeMin,
                CycleTimeMin = o.CycleTimeMin,
                NcProgramReference = o.NcProgramReference,
                Tooling = o.Tooling,
                Subcontractor = o.Subcontractor,
            }).ToList(),
            Version = r.Version,
        };
    }
}

/// <summary>« Nouvel indice ».</summary>
public sealed class NewRevisionInput
{
    /// <summary>Indice dupliqué (null = indice vide).</summary>
    public Guid? SourceRevisionId { get; set; }

    public string Index { get; set; } = string.Empty;

    public ChangeOrigin ChangeOrigin { get; set; } = ChangeOrigin.CustomerRequest;

    public string? ChangeDescription { get; set; }

    /// <summary>Documents de l'indice source à reprendre (case à cocher par document).</summary>
    public List<Guid> DocumentIdsToCopy { get; set; } = [];
}

/// <summary>Entrée de l'historique d'une pièce.</summary>
public sealed record HistoryEntry(DateTimeOffset At, string? UserName, string EntityType, string Action, IReadOnlyList<FieldChange> Changes, string? Note);

/// <summary>Champ modifié (ancienne → nouvelle valeur).</summary>
public sealed record FieldChange(string Field, string? OldValue, string? NewValue);

/// <summary>Types de contenu servis sans risque (aperçu intégré).</summary>
public static class DocumentContentTypes
{
    public const string Pdf = "application/pdf";
    public const string OctetStream = "application/octet-stream";

    private static readonly HashSet<string> SafeImages = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/gif", "image/webp", "image/bmp",
    };

    public static bool IsImage(string? contentType) => contentType is not null && SafeImages.Contains(contentType);

    public static bool IsPdf(string? contentType) => string.Equals(contentType, Pdf, StringComparison.OrdinalIgnoreCase);

    public static bool IsPreviewable(string? contentType) => IsImage(contentType) || IsPdf(contentType);

    /// <summary>Type de contenu à mémoriser à partir de l'extension (jamais de type exécutable par le navigateur).</summary>
    public static string FromFileName(string fileName)
    {
        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        return extension switch
        {
            ".pdf" => Pdf,
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".bmp" => "image/bmp",
            ".txt" or ".nc" or ".h" or ".iso" or ".mpf" or ".gcode" => "text/plain",
            _ => OctetStream,
        };
    }
}
