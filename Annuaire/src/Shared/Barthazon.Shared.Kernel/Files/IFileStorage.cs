namespace Barthazon.Shared.Kernel.Files;

/// <summary>Résultat d'un enregistrement de fichier.</summary>
public sealed record StoredFile(string StorageKey, long SizeBytes, string Sha256);

/// <summary>
/// Stockage des fichiers (plans, modèles, programmes…). Jamais de binaire en base :
/// la base ne conserve que la clé de stockage. Lecture et écriture en flux.
/// </summary>
public interface IFileStorage
{
    /// <summary>Enregistre le flux sous la clé donnée (chemin relatif, séparateur « / ») et calcule l'empreinte SHA-256.</summary>
    /// <exception cref="FileTooLargeException">Si le flux dépasse <paramref name="maxSizeBytes"/>.</exception>
    Task<StoredFile> SaveAsync(string storageKey, Stream content, long maxSizeBytes, CancellationToken cancellationToken = default);

    Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default);

    Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default);
}

/// <summary>Le fichier envoyé dépasse la taille maximale autorisée.</summary>
public sealed class FileTooLargeException : Exception
{
    public FileTooLargeException()
    {
    }

    public FileTooLargeException(string message)
        : base(message)
    {
    }

    public FileTooLargeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>Limites d'envoi de fichiers (configuration « FileStorage »).</summary>
public sealed record FileStorageLimits(long MaxFileSizeBytes)
{
    public int MaxFileSizeMb => (int)(MaxFileSizeBytes / 1024 / 1024);
}
