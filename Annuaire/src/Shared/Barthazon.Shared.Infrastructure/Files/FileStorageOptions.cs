namespace Barthazon.Shared.Infrastructure.Files;

/// <summary>Section de configuration « FileStorage ».</summary>
public sealed class FileStorageOptions
{
    public const string SectionName = "FileStorage";

    /// <summary>Dossier racine (peut être sur un partage réseau).</summary>
    public string RootPath { get; set; } = string.Empty;

    /// <summary>Taille maximale d'un fichier, en Mo.</summary>
    public int MaxFileSizeMb { get; set; } = 100;

    public long MaxFileSizeBytes => MaxFileSizeMb * 1024L * 1024L;
}
