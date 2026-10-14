using Barthazon.Shared.Kernel.Printing;
using Barthazon.Shared.Kernel.Settings;

namespace Barthazon.Stock.UI.Services;

/// <summary>
/// QR codes du stock : adresse publique + route, lisibles par l'appareil photo natif du téléphone. Lancement → sa fiche ;
/// article → saisie rapide pré-remplie ; emplacement → son contenu ; caisse de pièces finies → la pièce.
/// </summary>
public sealed class StockQrCodes(IApplicationSettingsStore settings, IQrCodeGenerator generator)
{
    public Task<string> BatchAsync(string number) => SvgAsync($"stock/lancements/{Uri.EscapeDataString(number)}");

    public Task<string> ItemAsync(string code) => SvgAsync($"stock/saisie?article={Uri.EscapeDataString(code)}");

    public Task<string> LocationAsync(string code) => SvgAsync($"stock/emplacements/{Uri.EscapeDataString(code)}");

    public Task<string> PartAsync(string reference) => SvgAsync($"stock/pieces/{Uri.EscapeDataString(reference)}");

    private async Task<string> SvgAsync(string route)
    {
        var baseUrl = (await settings.GetAsync()).PublicBaseUrl.TrimEnd('/');
        return generator.GenerateSvg($"{baseUrl}/{route}");
    }
}
