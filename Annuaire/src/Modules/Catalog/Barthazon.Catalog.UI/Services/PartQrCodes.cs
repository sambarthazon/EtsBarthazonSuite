using Barthazon.Shared.Kernel.Printing;
using Barthazon.Shared.Kernel.Settings;

namespace Barthazon.Catalog.UI.Services;

/// <summary>
/// QR code d'une pièce : il contient l'adresse de la fiche (adresse publique + route), lisible par l'appareil
/// photo natif du téléphone. Il pointe vers la pièce (pas vers un indice) : il mène toujours à l'indice en vigueur.
/// </summary>
public sealed class PartQrCodes(IApplicationSettingsStore settings, IQrCodeGenerator generator)
{
    public async Task<string> UrlAsync(string internalReference)
    {
        var baseUrl = (await settings.GetAsync()).PublicBaseUrl.TrimEnd('/');
        return $"{baseUrl}/pieces/{Uri.EscapeDataString(internalReference)}";
    }

    public async Task<(string Url, string Svg)> ForPartAsync(string internalReference)
    {
        var url = await UrlAsync(internalReference);
        return (url, generator.GenerateSvg(url));
    }
}
