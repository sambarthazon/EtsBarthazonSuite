using System.Globalization;
using System.Text;

namespace Barthazon.Shared.UI.Theme;

/// <summary>
/// CHARTE GRAPHIQUE — SEUL ENDROIT OÙ LES COULEURS SONT DÉFINIES.
/// <para>
/// Ce fichier alimente le thème MudBlazor (clair et sombre) et les variables CSS
/// (<c>--bz-primary</c>, <c>--bz-accent</c>…) utilisées par les feuilles de style.
/// Aucune couleur ne doit être écrite en dur ailleurs (ni dans les .razor, ni dans les .css).
/// Changer la charte = modifier ce seul fichier.
/// </para>
/// </summary>
public static class BrandTokens
{
    /// <summary>Jeu de couleurs d'un mode (clair ou sombre).</summary>
    public sealed record ColorSet(
        string Primary,
        string PrimaryContrast,
        string Accent,
        string AccentContrast,
        string AccentText,
        string Background,
        string Surface,
        string SurfaceAlt,
        string Text,
        string TextSecondary,
        string Success,
        string Warning,
        string Error,
        string Info,
        string Disabled,
        string Lines,
        string AppBar,
        string AppBarText,
        string Highlight,
        string Shadow,
        string Overlay);

    /// <summary>Mode clair.</summary>
    public static readonly ColorSet Light = new(
        Primary: "#1E3A5F",
        PrimaryContrast: "#FFFFFF",
        Accent: "#E07A1F",
        AccentContrast: "#FFFFFF",
        // Accent utilisé comme couleur de texte : assombri pour un contraste AA (4,5:1) sur fond clair.
        AccentText: "#B85C12",
        Background: "#F4F6F8",
        Surface: "#FFFFFF",
        SurfaceAlt: "#EEF2F6",
        Text: "#1B2430",
        TextSecondary: "#4A5868",
        Success: "#2E7D32",
        // #ED6C02 (palette d'origine) n'atteint que 3,1:1 sur blanc : assombri pour les textes d'alerte (AA).
        Warning: "#B45309",
        Error: "#C62828",
        Info: "#0277BD",
        Disabled: "#9AA5B1",
        Lines: "#D5DCE3",
        AppBar: "#1E3A5F",
        AppBarText: "#FFFFFF",
        Highlight: "#FFE08A",
        Shadow: "rgba(16, 32, 52, 0.10)",
        Overlay: "rgba(244, 246, 248, 0.55)");

    /// <summary>Mode sombre.</summary>
    public static readonly ColorSet Dark = new(
        Primary: "#7FB0E0",
        PrimaryContrast: "#0E1620",
        Accent: "#F29A4A",
        AccentContrast: "#1B1206",
        AccentText: "#F29A4A",
        Background: "#12171D",
        Surface: "#1B232C",
        SurfaceAlt: "#232D38",
        Text: "#E6EAEE",
        TextSecondary: "#AAB6C2",
        Success: "#66BB6A",
        Warning: "#FFA726",
        Error: "#EF5350",
        Info: "#4FC3F7",
        Disabled: "#5C6773",
        Lines: "#2F3B48",
        AppBar: "#16202A",
        AppBarText: "#E6EAEE",
        Highlight: "#7A5D00",
        Shadow: "rgba(0, 0, 0, 0.45)",
        Overlay: "rgba(18, 23, 29, 0.55)");

    /// <summary>Couleurs utilisées à l'impression (fiche pièce, étiquettes) : toujours noir sur blanc.</summary>
    public static readonly (string Text, string Background, string Lines) Print = ("#000000", "#FFFFFF", "#888888");

    /// <summary>Polices (embarquées dans l'application, voir wwwroot/fonts).</summary>
    public static readonly string[] FontFamily = ["Inter", "Segoe UI", "Roboto", "Helvetica Neue", "Arial", "sans-serif"];

    /// <summary>Rayon des angles.</summary>
    public const string BorderRadius = "10px";

    /// <summary>Rayon des pastilles et petits éléments.</summary>
    public const string BorderRadiusSmall = "6px";

    /// <summary>Taille minimale des zones tactiles (atelier : doigts, gants).</summary>
    public const string TouchTarget = "48px";

    /// <summary>Hauteur de la barre de navigation basse (téléphone).</summary>
    public const string BottomBarHeight = "64px";

    /// <summary>Variables CSS d'un mode, à injecter dans <c>:root</c>.</summary>
    public static string ToCssVariables(ColorSet colors)
    {
        ArgumentNullException.ThrowIfNull(colors);
        var builder = new StringBuilder();
        void Add(string name, string value) => builder.Append(CultureInfo.InvariantCulture, $"--bz-{name}:{value};");
        Add("primary", colors.Primary);
        Add("primary-contrast", colors.PrimaryContrast);
        Add("accent", colors.Accent);
        Add("accent-contrast", colors.AccentContrast);
        Add("accent-text", colors.AccentText);
        Add("background", colors.Background);
        Add("surface", colors.Surface);
        Add("surface-alt", colors.SurfaceAlt);
        Add("text", colors.Text);
        Add("text-secondary", colors.TextSecondary);
        Add("success", colors.Success);
        Add("warning", colors.Warning);
        Add("error", colors.Error);
        Add("info", colors.Info);
        Add("disabled", colors.Disabled);
        Add("lines", colors.Lines);
        Add("appbar", colors.AppBar);
        Add("appbar-text", colors.AppBarText);
        Add("highlight", colors.Highlight);
        Add("shadow", colors.Shadow);
        Add("overlay", colors.Overlay);
        Add("print-text", Print.Text);
        Add("print-background", Print.Background);
        Add("print-lines", Print.Lines);
        Add("radius", BorderRadius);
        Add("radius-sm", BorderRadiusSmall);
        Add("touch", TouchTarget);
        Add("bottom-bar", BottomBarHeight);
        Add("font", string.Join(',', FontFamily.Select(f => f.Contains(' ', StringComparison.Ordinal) ? $"\"{f}\"" : f)));
        return builder.ToString();
    }
}
