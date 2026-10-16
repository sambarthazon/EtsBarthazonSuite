using Barthazon.Shared.Kernel.Domain;

namespace Barthazon.Stock.Core.Domain;

/// <summary>Nature d'un emplacement de stockage.</summary>
public enum StockLocationKind
{
    /// <summary>Magasin.</summary>
    Storage,

    /// <summary>Atelier (pièces en cours).</summary>
    Workshop,

    /// <summary>Expédition.</summary>
    Shipping,

    /// <summary>Chez un sous-traitant.</summary>
    Subcontractor,

    Other,
}

/// <summary>Lieu de rangement (magasin, atelier, expédition, sous-traitant). Le code est imprimé en QR.</summary>
public sealed class StockLocation : AuditableEntity
{
    public const int CodeMaxLength = 30;
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 1000;

    private StockLocation()
    {
    }

    public StockLocation(string code, string name, StockLocationKind kind, int sortOrder = 0)
    {
        Code = NormalizeCode(code);
        Name = name.Trim();
        Kind = kind;
        SortOrder = sortOrder;
    }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public StockLocationKind Kind { get; set; }

    public string? Description { get; set; }

    public int SortOrder { get; set; }

    /// <summary>Codes en majuscules, sans espaces autour (ils sont saisis à la main ou lus dans un QR).</summary>
    public static string NormalizeCode(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return code.Trim().ToUpperInvariant();
    }
}
