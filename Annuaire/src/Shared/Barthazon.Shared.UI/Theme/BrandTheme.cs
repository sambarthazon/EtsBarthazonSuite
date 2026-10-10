using MudBlazor;

namespace Barthazon.Shared.UI.Theme;

/// <summary>Construit le thème MudBlazor à partir de <see cref="BrandTokens"/>.</summary>
public static class BrandTheme
{
    public static MudTheme Theme { get; } = Create();

    private static MudTheme Create()
    {
        var light = BrandTokens.Light;
        var dark = BrandTokens.Dark;
        return new MudTheme
        {
            PaletteLight = new PaletteLight
            {
                Primary = light.Primary,
                PrimaryContrastText = light.PrimaryContrast,
                Secondary = light.Accent,
                SecondaryContrastText = light.AccentContrast,
                Tertiary = light.Info,
                Info = light.Info,
                Success = light.Success,
                Warning = light.Warning,
                Error = light.Error,
                Background = light.Background,
                BackgroundGray = light.SurfaceAlt,
                Surface = light.Surface,
                TextPrimary = light.Text,
                TextSecondary = light.TextSecondary,
                TextDisabled = light.Disabled,
                ActionDisabled = light.Disabled,
                AppbarBackground = light.AppBar,
                AppbarText = light.AppBarText,
                DrawerBackground = light.Surface,
                DrawerText = light.Text,
                DrawerIcon = light.TextSecondary,
                LinesDefault = light.Lines,
                LinesInputs = light.TextSecondary,
                TableLines = light.Lines,
                Divider = light.Lines,
            },
            PaletteDark = new PaletteDark
            {
                Primary = dark.Primary,
                PrimaryContrastText = dark.PrimaryContrast,
                Secondary = dark.Accent,
                SecondaryContrastText = dark.AccentContrast,
                Tertiary = dark.Info,
                Info = dark.Info,
                Success = dark.Success,
                Warning = dark.Warning,
                Error = dark.Error,
                Background = dark.Background,
                BackgroundGray = dark.SurfaceAlt,
                Surface = dark.Surface,
                TextPrimary = dark.Text,
                TextSecondary = dark.TextSecondary,
                TextDisabled = dark.Disabled,
                ActionDisabled = dark.Disabled,
                AppbarBackground = dark.AppBar,
                AppbarText = dark.AppBarText,
                DrawerBackground = dark.Surface,
                DrawerText = dark.Text,
                DrawerIcon = dark.TextSecondary,
                LinesDefault = dark.Lines,
                LinesInputs = dark.TextSecondary,
                TableLines = dark.Lines,
                Divider = dark.Lines,
            },
            Typography = new Typography
            {
                Default = new DefaultTypography { FontFamily = BrandTokens.FontFamily },
                H1 = new H1Typography { FontFamily = BrandTokens.FontFamily, FontWeight = "700", FontSize = "2.25rem" },
                H2 = new H2Typography { FontFamily = BrandTokens.FontFamily, FontWeight = "700", FontSize = "2rem" },
                H3 = new H3Typography { FontFamily = BrandTokens.FontFamily, FontWeight = "700", FontSize = "1.75rem" },
                H4 = new H4Typography { FontFamily = BrandTokens.FontFamily, FontWeight = "600", FontSize = "1.5rem" },
                H5 = new H5Typography { FontFamily = BrandTokens.FontFamily, FontWeight = "600", FontSize = "1.25rem" },
                H6 = new H6Typography { FontFamily = BrandTokens.FontFamily, FontWeight = "600", FontSize = "1.1rem" },
                Button = new ButtonTypography { FontFamily = BrandTokens.FontFamily, FontWeight = "600", TextTransform = "none" },
            },
            LayoutProperties = new LayoutProperties
            {
                DefaultBorderRadius = BrandTokens.BorderRadius,
                AppbarHeight = "64px",
                DrawerWidthLeft = "264px",
            },
        };
    }
}
