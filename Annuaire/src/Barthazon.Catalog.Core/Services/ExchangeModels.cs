using Barthazon.Catalog.Contracts;

namespace Barthazon.Catalog.Core.Services;

/// <summary>Colonnes du fichier d'échange (import et export utilisent le même format : un export se réimporte).</summary>
public static class ExchangeColumns
{
    public const string InternalReference = "Référence interne";
    public const string Designation = "Désignation";
    public const string CustomerCode = "Code client";
    public const string CustomerName = "Nom client";
    public const string CustomerReference = "Référence client";
    public const string Family = "Famille";
    public const string Status = "Statut";
    public const string Index = "Indice";
    public const string RevisionStatus = "Statut de l'indice";
    public const string Material = "Matière";
    public const string Grade = "Nuance";
    public const string BlankShape = "Forme du brut";
    public const string BlankDimensions = "Dimensions du brut";
    public const string Tags = "Étiquettes";
    public const string Description = "Description";
    public const string WorkshopNotes = "Consignes atelier";
    public const string GeneralTolerance = "Tolérance générale";
    public const string UpdatedAt = "Modifiée le";

    /// <summary>Colonnes de l'import, dans l'ordre du modèle.</summary>
    public static IReadOnlyList<string> Import { get; } =
    [
        InternalReference, Designation, CustomerCode, CustomerName, CustomerReference, Family, Status, Index,
        Material, Grade, BlankShape, BlankDimensions, Tags, Description, WorkshopNotes, GeneralTolerance,
    ];

    /// <summary>Colonnes obligatoires.</summary>
    public static IReadOnlyList<string> Required { get; } = [InternalReference, Designation, CustomerCode];

    /// <summary>Longueur maximale de chaque colonne importée (mêmes limites que la saisie).</summary>
    public static IReadOnlyList<(string Column, int Max)> MaxLengths { get; } =
    [
        (InternalReference, 50), (Designation, 200), (CustomerCode, 20), (CustomerName, 200), (CustomerReference, 100),
        (Family, 100), (Index, 10), (Material, 150), (Grade, 100), (BlankDimensions, 100), (Description, 4000),
        (WorkshopNotes, 4000), (GeneralTolerance, 200),
    ];

    /// <summary>Colonnes de l'export (celles de l'import + informations).</summary>
    public static IReadOnlyList<string> Export { get; } = [.. Import.Take(8), RevisionStatus, .. Import.Skip(8), UpdatedAt];
}

/// <summary>Ligne lue dans un fichier (valeurs par nom de colonne normalisé).</summary>
public sealed record SpreadsheetRow(int LineNumber, IReadOnlyDictionary<string, string> Values)
{
    public string? Get(string column) => Values.TryGetValue(column, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}

/// <summary>Contenu lu (ou erreur de lecture).</summary>
public sealed record SpreadsheetContent(IReadOnlyList<string> Columns, IReadOnlyList<SpreadsheetRow> Rows);

/// <summary>Lecture / écriture des fichiers Excel et CSV (implémenté avec ClosedXML).</summary>
public interface IPartSpreadsheet
{
    /// <summary>Lit un fichier .xlsx ou .csv ; les en-têtes sont rapprochés des colonnes connues avec tolérance.</summary>
    SpreadsheetContent Read(Stream content, string fileName, IReadOnlyList<string> knownColumns);

    /// <summary>Écrit un classeur Excel (une feuille, en-têtes en gras, filtres automatiques).</summary>
    byte[] Write(string sheetName, IReadOnlyList<string> columns, IEnumerable<IReadOnlyList<object?>> rows);
}

/// <summary>Action prévue pour une ligne d'import.</summary>
public enum ImportAction
{
    Create,
    Update,
    Error,
}

/// <summary>Ligne analysée (aperçu avant confirmation).</summary>
public sealed record ImportRowPreview(
    int LineNumber,
    string? InternalReference,
    string? Designation,
    string? CustomerCode,
    ImportAction Action,
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings);

/// <summary>Aperçu d'un import.</summary>
public sealed record ImportPreview(
    IReadOnlyList<ImportRowPreview> Rows,
    IReadOnlyList<string> MissingCustomers,
    IReadOnlyList<string> MissingMaterials,
    IReadOnlyList<string> MissingFamilies,
    IReadOnlyList<string> FileErrors)
{
    public int CreateCount => Rows.Count(r => r.Action == ImportAction.Create);

    public int UpdateCount => Rows.Count(r => r.Action == ImportAction.Update);

    public int ErrorCount => Rows.Count(r => r.Action == ImportAction.Error);
}

/// <summary>Options de l'import : création automatique des éléments inconnus.</summary>
public sealed record ImportOptions(bool CreateMissingCustomers = true, bool CreateMissingMaterials = true, bool CreateMissingFamilies = true);

/// <summary>Résultat d'un import.</summary>
public sealed record ImportResult(int Created, int Updated, IReadOnlyList<ImportRowPreview> Failed);

/// <summary>Fichier produit (export, modèle).</summary>
public sealed record ExportFile(string FileName, string ContentType, byte[] Content)
{
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

/// <summary>Valeurs françaises acceptées dans les fichiers.</summary>
internal static class ExchangeValues
{
    public static readonly IReadOnlyDictionary<string, PartStatus> Statuses = new Dictionary<string, PartStatus>(StringComparer.Ordinal)
    {
        ["prototype"] = PartStatus.Prototype,
        ["active"] = PartStatus.Active,
        ["actif"] = PartStatus.Active,
        ["ensommeil"] = PartStatus.Dormant,
        ["sommeil"] = PartStatus.Dormant,
        ["dormant"] = PartStatus.Dormant,
        ["obsolete"] = PartStatus.Obsolete,
    };

    public static readonly IReadOnlyDictionary<string, BlankShape> Shapes = new Dictionary<string, BlankShape>(StringComparer.Ordinal)
    {
        ["rond"] = BlankShape.Round,
        ["round"] = BlankShape.Round,
        ["carre"] = BlankShape.Square,
        ["plat"] = BlankShape.Flat,
        ["hexagonal"] = BlankShape.Hexagonal,
        ["hexagone"] = BlankShape.Hexagonal,
        ["tube"] = BlankShape.Tube,
        ["tole"] = BlankShape.Sheet,
        ["bloc"] = BlankShape.Block,
        ["mouleforge"] = BlankShape.CastOrForged,
        ["moule"] = BlankShape.CastOrForged,
        ["forge"] = BlankShape.CastOrForged,
        ["autre"] = BlankShape.Other,
    };

    public static string StatusLabel(PartStatus status) => status switch
    {
        PartStatus.Prototype => "Prototype",
        PartStatus.Active => "Active",
        PartStatus.Dormant => "En sommeil",
        _ => "Obsolète",
    };

    public static string RevisionStatusLabel(Contracts.RevisionStatus status) => status switch
    {
        Contracts.RevisionStatus.Draft => "Brouillon",
        Contracts.RevisionStatus.InForce => "En vigueur",
        Contracts.RevisionStatus.Superseded => "Remplacé",
        _ => "Annulé",
    };

    public static string ShapeLabel(BlankShape shape) => shape switch
    {
        BlankShape.Round => "Rond",
        BlankShape.Square => "Carré",
        BlankShape.Flat => "Plat",
        BlankShape.Hexagonal => "Hexagonal",
        BlankShape.Tube => "Tube",
        BlankShape.Sheet => "Tôle",
        BlankShape.Block => "Bloc",
        BlankShape.CastOrForged => "Moulé / forgé",
        _ => "Autre",
    };
}
