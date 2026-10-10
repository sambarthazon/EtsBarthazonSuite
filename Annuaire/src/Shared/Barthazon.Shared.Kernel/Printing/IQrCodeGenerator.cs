namespace Barthazon.Shared.Kernel.Printing;

/// <summary>Génération de QR codes côté serveur.</summary>
public interface IQrCodeGenerator
{
    /// <summary>QR code au format SVG (balise &lt;svg&gt; complète).</summary>
    string GenerateSvg(string content);

    /// <summary>QR code au format PNG.</summary>
    byte[] GeneratePng(string content, int pixelsPerModule = 10);
}
