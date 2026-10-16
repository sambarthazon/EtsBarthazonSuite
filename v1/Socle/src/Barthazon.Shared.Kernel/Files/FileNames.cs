using System.Globalization;
using System.Text;

namespace Barthazon.Shared.Kernel.Files;

/// <summary>Outils sur les noms de fichiers.</summary>
public static class FileNames
{
    /// <summary>Assainit un nom de fichier : sans accents, sans caractères spéciaux, longueur limitée.</summary>
    public static string Sanitize(string fileName, int maxLength = 80)
    {
        ArgumentNullException.ThrowIfNull(fileName);
        var name = Path.GetFileName(fileName.Replace('\\', '/'));
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsAsciiLetterOrDigit(c) || c is '.' or '-' or '_' ? c : '_');
        }

        var result = builder.ToString().Trim('.', '_');
        if (result.Length == 0)
        {
            result = "fichier";
        }

        if (result.Length > maxLength)
        {
            var extension = Path.GetExtension(result);
            if (extension.Length > 10)
            {
                extension = string.Empty;
            }

            result = result[..(maxLength - extension.Length)] + extension;
        }

        return result;
    }
}
