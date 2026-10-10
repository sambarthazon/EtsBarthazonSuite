using System.Globalization;
using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.UI.Resources;
using Barthazon.Shared.UI.Components;

namespace Barthazon.Catalog.UI.Components;

/// <summary>Libellés français des énumérations et des champs de l'annuaire (ressources CatalogTexts).</summary>
public static class CatalogLabels
{
    public static string Label(this TagColor value) => Get("TagColor_" + value) ?? value.ToString();

    public static string Label(this PartStatus value) => Get("PartStatus_" + value) ?? value.ToString();

    public static string Label(this RevisionStatus value) => Get("RevisionStatus_" + value) ?? value.ToString();

    public static string Label(this ChangeOrigin value) => Get("ChangeOrigin_" + value) ?? value.ToString();

    public static string Label(this BlankShape value) => Get("BlankShape_" + value) ?? value.ToString();

    public static string Label(this OperationType value) => Get("OperationType_" + value) ?? value.ToString();

    public static string Label(this DocumentType value) => Get("DocumentType_" + value) ?? value.ToString();

    public static string Label(this DocumentKind value) => Get("DocumentKind_" + value) ?? value.ToString();

    public static string Label(this PartLinkType value, bool inverse = false) =>
        Get((inverse ? "PartLinkTypeInverse_" : "PartLinkType_") + value) ?? value.ToString();

    /// <summary>Libellé d'un champ (journal, comparaison).</summary>
    public static string FieldLabel(string field) => Get("Field_" + field) ?? field;

    public static string EntityLabel(string entityType) => Get("Entity_" + entityType) ?? entityType;

    public static string ActionLabel(string action) => Get("Action_" + action) ?? action;

    /// <summary>Valeur d'énumération stockée dans le journal (nom anglais) traduite si possible.</summary>
    public static string? ValueLabel(string field, string? value)
    {
        if (value is null)
        {
            return null;
        }

        var prefix = field switch
        {
            "Status" => Enum.TryParse<RevisionStatus>(value, out _) && !Enum.TryParse<PartStatus>(value, out _) ? "RevisionStatus_" : "PartStatus_",
            "ChangeOrigin" => "ChangeOrigin_",
            "BlankShape" => "BlankShape_",
            "Type" => Enum.TryParse<OperationType>(value, out _) ? "OperationType_" : "DocumentType_",
            "Kind" => "DocumentKind_",
            _ => null,
        };
        var translated = prefix is null ? null : Get(prefix + value) ?? (field == "Status" ? Get("RevisionStatus_" + value) : null);
        return translated ?? value.Trim('"');
    }

    public static StatusTone Tone(this PartStatus status) => status switch
    {
        PartStatus.Active => StatusTone.Success,
        PartStatus.Prototype => StatusTone.Info,
        PartStatus.Dormant => StatusTone.Warning,
        _ => StatusTone.Error,
    };

    public static StatusTone Tone(this RevisionStatus status) => status switch
    {
        RevisionStatus.InForce => StatusTone.Success,
        RevisionStatus.Draft => StatusTone.Warning,
        RevisionStatus.Superseded => StatusTone.Neutral,
        _ => StatusTone.Error,
    };

    /// <summary>Classe CSS de la teinte (couleurs définies dans la charte).</summary>
    public static string CssClass(this TagColor color) => "bz-tag bz-tag-" + color.ToString().ToLowerInvariant();

    public static string FileSize(long? bytes) => bytes switch
    {
        null => string.Empty,
        < 1024 => $"{bytes} o",
        < 1024 * 1024 => (bytes.Value / 1024d).ToString("0.#", CultureInfo.CurrentCulture) + " Ko",
        _ => (bytes.Value / 1024d / 1024d).ToString("0.#", CultureInfo.CurrentCulture) + " Mo",
    };

    public static string Number(decimal? value) => value?.ToString("0.###", CultureInfo.CurrentCulture) ?? string.Empty;

    internal static string? Get(string key) => CatalogTexts.ResourceManager.GetString(key, CultureInfo.CurrentUICulture);
}
