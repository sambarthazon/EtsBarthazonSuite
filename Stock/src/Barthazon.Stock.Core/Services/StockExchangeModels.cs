using System.Globalization;
using Barthazon.Catalog.Contracts;
using Barthazon.Shared.Kernel.Text;
using Barthazon.Stock.Core.Domain;

namespace Barthazon.Stock.Core.Services;

/// <summary>Ligne lue dans une feuille (valeurs par nom de colonne connu).</summary>
public sealed record SheetRow(int LineNumber, IReadOnlyDictionary<string, string> Values)
{
    public string? Get(string column) => Values.TryGetValue(column, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;
}

/// <summary>Feuille lue : colonnes reconnues et lignes non vides.</summary>
public sealed record SheetContent(string Name, IReadOnlyList<string> Columns, IReadOnlyList<SheetRow> Rows);

/// <summary>Feuille à écrire.</summary>
public sealed record SheetData(string Name, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<object?>> Rows);

/// <summary>Fichier produit (export, modèle).</summary>
public sealed record StockFile(string FileName, string ContentType, byte[] Content)
{
    public const string ExcelContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet";
}

/// <summary>Lecture / écriture de classeurs Excel à plusieurs feuilles (implémenté avec ClosedXML).</summary>
public interface IStockSpreadsheet
{
    /// <summary>
    /// Lit un classeur .xlsx. Les feuilles sont reconnues par leur nom (<paramref name="sheets"/> : nom → colonnes
    /// connues), les en-têtes rapprochés des colonnes connues avec tolérance (casse, accents, espaces, « * »).
    /// Une feuille absente du fichier n'est pas renvoyée.
    /// </summary>
    IReadOnlyList<SheetContent> Read(Stream content, string fileName, IReadOnlyDictionary<string, IReadOnlyList<string>> sheets);

    /// <summary>Écrit un classeur (en-têtes en gras, filtres automatiques, première ligne figée).</summary>
    byte[] Write(IReadOnlyList<SheetData> sheets);
}

/// <summary>Colonnes des feuilles d'import du stock initial ; l'export de l'état du stock utilise les mêmes.</summary>
public static class StockExchangeColumns
{
    public const string ItemsSheet = "Articles";
    public const string FinishedPartsSheet = "Pièces finies";
    public const string WorkInProgressSheet = "En-cours";

    public const string Code = "Code";
    public const string Designation = "Désignation";
    public const string Kind = "Type";
    public const string Material = "Matière";
    public const string Shape = "Forme";
    public const string Dimensions = "Dimensions";
    public const string Unit = "Unité";
    public const string Owner = "Propriétaire";
    public const string Threshold = "Seuil";
    public const string Location = "Emplacement";
    public const string Quantity = "Quantité";
    public const string Lot = "Lot";
    public const string UnitCost = "Prix de référence";
    public const string PartReference = "Référence pièce";
    public const string Index = "Indice";
    public const string WaitingOperation = "En attente de l'opération n°";
    public const string Batch = "Lancement";

    public static IReadOnlyList<string> Items { get; } =
        [Code, Designation, Kind, Material, Shape, Dimensions, Unit, Owner, Threshold, Location, Quantity, Lot, UnitCost];

    public static IReadOnlyList<string> FinishedParts { get; } = [PartReference, Index, Location, Quantity, Lot];

    public static IReadOnlyList<string> WorkInProgress { get; } = [PartReference, Index, WaitingOperation, Quantity, Location];

    /// <summary>Feuilles reconnues à l'import.</summary>
    public static IReadOnlyDictionary<string, IReadOnlyList<string>> ImportSheets { get; } = new Dictionary<string, IReadOnlyList<string>>
    {
        [ItemsSheet] = Items,
        [FinishedPartsSheet] = FinishedParts,
        [WorkInProgressSheet] = [.. WorkInProgress, Batch],
    };
}

/// <summary>Valeurs françaises acceptées et écrites dans les fichiers.</summary>
internal static class StockExchangeValues
{
    private static readonly Dictionary<string, StockItemKind> Kinds = new(StringComparer.Ordinal)
    {
        ["matiere"] = StockItemKind.RawMaterial,
        ["matierepremiere"] = StockItemKind.RawMaterial,
        ["brut"] = StockItemKind.Blank,
        ["debit"] = StockItemKind.Blank,
        ["autre"] = StockItemKind.Other,
    };

    private static readonly Dictionary<string, StockUnit> Units = new(StringComparer.Ordinal)
    {
        ["u"] = StockUnit.Piece,
        ["unite"] = StockUnit.Piece,
        ["pc"] = StockUnit.Piece,
        ["pce"] = StockUnit.Piece,
        ["piece"] = StockUnit.Piece,
        ["pieces"] = StockUnit.Piece,
        ["m"] = StockUnit.Meter,
        ["metre"] = StockUnit.Meter,
        ["metres"] = StockUnit.Meter,
        ["ml"] = StockUnit.Meter,
        ["kg"] = StockUnit.Kilogram,
        ["kilogramme"] = StockUnit.Kilogram,
        ["m2"] = StockUnit.SquareMeter,
        ["m²"] = StockUnit.SquareMeter,
        ["metrecarre"] = StockUnit.SquareMeter,
    };

    private static readonly Dictionary<string, BlankShape> Shapes = new(StringComparer.Ordinal)
    {
        ["rond"] = BlankShape.Round,
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

    public static StockItemKind? Kind(string value) => Kinds.TryGetValue(SearchNormalizer.Compact(value), out var kind) ? kind : null;

    public static StockUnit? Unit(string value)
    {
        var trimmed = value.Trim().ToLowerInvariant();
        return Units.TryGetValue(trimmed, out var unit) || Units.TryGetValue(SearchNormalizer.Compact(value), out unit) ? unit : null;
    }

    public static BlankShape? Shape(string value) => Shapes.TryGetValue(SearchNormalizer.Compact(value), out var shape) ? shape : null;

    /// <summary>Nombre écrit « 1,5 », « 1.5 » ou « 1 280,5 ».</summary>
    public static decimal? Number(string value)
    {
        var cleaned = value.Replace(" ", string.Empty, StringComparison.Ordinal).Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal).Replace(',', '.');
        return decimal.TryParse(cleaned, NumberStyles.Number, CultureInfo.InvariantCulture, out var number) ? number : null;
    }

    public static string KindLabel(StockItemKind kind) => kind switch
    {
        StockItemKind.RawMaterial => "Matière",
        StockItemKind.Blank => "Brut",
        _ => "Autre",
    };

    public static string UnitLabel(StockUnit unit) => unit switch
    {
        StockUnit.Piece => "u",
        StockUnit.Meter => "m",
        StockUnit.Kilogram => "kg",
        _ => "m²",
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

    public static string TypeLabel(StockTransactionType type) => type switch
    {
        StockTransactionType.ItemReceipt => "Réception",
        StockTransactionType.ItemIssue => "Sortie d'article",
        StockTransactionType.Launch => "Lancement",
        StockTransactionType.OperationDone => "Opération réalisée",
        StockTransactionType.OperationSkipped => "Opération non réalisée",
        StockTransactionType.Scrap => "Rebut",
        StockTransactionType.Rework => "Reprise",
        StockTransactionType.Transfer => "Transfert",
        StockTransactionType.Shipment => "Sortie de pièces finies",
        StockTransactionType.PartReceipt => "Entrée de pièces finies",
        StockTransactionType.Adjustment => "Correction",
        _ => "Annulation",
    };

    public static string ReasonLabel(StockReason reason) => reason switch
    {
        StockReason.Dimension => "Cote",
        StockReason.Aspect => "Aspect",
        StockReason.Material => "Matière",
        StockReason.Setup => "Réglage",
        StockReason.Handling => "Manutention",
        StockReason.Inventory => "Inventaire",
        StockReason.InitialStock => "Stock initial",
        _ => "Autre",
    };

    public static string StatusLabel(Stock.Contracts.BatchStatus status) => status switch
    {
        Stock.Contracts.BatchStatus.InProgress => "En cours",
        Stock.Contracts.BatchStatus.Completed => "Terminé",
        _ => "Annulé",
    };
}
