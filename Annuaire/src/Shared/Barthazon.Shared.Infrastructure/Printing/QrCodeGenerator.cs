using Barthazon.Shared.Kernel.Printing;
using QRCoder;

namespace Barthazon.Shared.Infrastructure.Printing;

/// <summary>QR codes générés côté serveur avec QRCoder (noir sur blanc, lisibles par l'appareil photo d'un téléphone).</summary>
public sealed class QrCodeGenerator : IQrCodeGenerator
{
    public string GenerateSvg(string content)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        using var svg = new SvgQRCode(data);
        return svg.GetGraphic(new System.Drawing.Size(256, 256), drawQuietZones: true, sizingMode: SvgQRCode.SizingMode.ViewBoxAttribute);
    }

    public byte[] GeneratePng(string content, int pixelsPerModule = 10)
    {
        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(content, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(pixelsPerModule, drawQuietZones: true);
    }
}
