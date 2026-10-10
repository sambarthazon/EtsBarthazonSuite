using System.Globalization;
using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.UI.Resources;

namespace Barthazon.Catalog.UI.Components;

/// <summary>Libellés français des énumérations de l'annuaire (ressources CatalogTexts).</summary>
public static class CatalogLabels
{
    public static string Label(this TagColor color) => Get("TagColor_" + color) ?? color.ToString();

    internal static string? Get(string key) => CatalogTexts.ResourceManager.GetString(key, CultureInfo.CurrentUICulture);

    /// <summary>Classe CSS de la teinte (couleurs définies dans la charte).</summary>
    public static string CssClass(this TagColor color) => "bz-tag bz-tag-" + color.ToString().ToLowerInvariant();
}
