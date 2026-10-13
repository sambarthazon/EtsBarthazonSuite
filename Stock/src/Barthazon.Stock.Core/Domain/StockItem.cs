using Barthazon.Catalog.Contracts;
using Barthazon.Shared.Kernel.Domain;
using Barthazon.Shared.Kernel.Text;

namespace Barthazon.Stock.Core.Domain;

/// <summary>Article : matière ou brut stocké, qui n'est pas une pièce de l'annuaire (section 7.2).</summary>
public sealed class StockItem : AuditableEntity
{
    public const int CodeMaxLength = 30;
    public const int DesignationMaxLength = 200;
    public const int DimensionsMaxLength = 100;
    public const int SupplierMaxLength = 100;
    public const int NotesMaxLength = 4000;

    private StockItem()
    {
    }

    public StockItem(string code, string designation, StockItemKind kind, StockUnit unit)
    {
        Code = NormalizeCode(code);
        Designation = designation.Trim();
        Kind = kind;
        Unit = unit;
        RefreshSearchText();
    }

    public string Code { get; set; } = string.Empty;

    public string Designation { get; set; } = string.Empty;

    public StockItemKind Kind { get; set; }

    public Guid? MaterialId { get; set; }

    public BlankShape? Shape { get; set; }

    public string? Dimensions { get; set; }

    public StockUnit Unit { get; set; }

    /// <summary>Vide = appartient à BARTHAZON ; sinon fourni par ce client.</summary>
    public Guid? OwnerCustomerId { get; set; }

    public decimal? MinimumThreshold { get; set; }

    public Guid? DefaultLocationId { get; set; }

    /// <summary>Prix unitaire de référence (€ HT / unité), pour une valeur indicative seulement.</summary>
    public decimal? ReferenceUnitCost { get; set; }

    public string? UsualSupplier { get; set; }

    /// <summary>Chaque réception porte un numéro de lot, chaque consommation précise le lot.</summary>
    public bool TrackLots { get; set; }

    public string? Notes { get; set; }

    public string SearchText { get; private set; } = string.Empty;

    /// <summary>Les pièces se comptent en entiers ; les autres unités acceptent trois décimales.</summary>
    public bool IsCountedInUnits => Unit == StockUnit.Piece;

    public void RefreshSearchText() =>
        SearchText = SearchNormalizer.BuildSearchText([Code, Designation, Dimensions, UsualSupplier]);

    public static string NormalizeCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return code.Trim().ToUpperInvariant();
    }
}
