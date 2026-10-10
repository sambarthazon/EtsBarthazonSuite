using System.Buffers;
using System.Security.Cryptography;
using Barthazon.Shared.Kernel.Files;

namespace Barthazon.Shared.Infrastructure.Files;

/// <summary>
/// Stockage des fichiers dans un dossier sur disque. Écriture en flux dans un fichier temporaire
/// puis renommage (pas de fichier à moitié écrit), empreinte SHA-256 calculée au passage.
/// </summary>
public sealed class FileSystemFileStorage : IFileStorage
{
    private const int BufferSize = 81920;
    private readonly string _root;

    public FileSystemFileStorage(FileStorageOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        _root = Path.GetFullPath(options.RootPath);
    }

    public async Task<StoredFile> SaveAsync(string storageKey, Stream content, long maxSizeBytes, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var path = Resolve(storageKey);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + ".upload-" + Guid.NewGuid().ToString("N");
        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        long total = 0;
        try
        {
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                int read;
                while ((read = await content.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken)) > 0)
                {
                    total += read;
                    if (total > maxSizeBytes)
                    {
                        throw new FileTooLargeException(maxSizeBytes.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }

                    hash.AppendData(buffer, 0, read);
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }

            File.Move(temporary, path, overwrite: true);
            return new StoredFile(storageKey, total, Convert.ToHexStringLower(hash.GetHashAndReset()));
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var path = Resolve(storageKey);
        Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);
        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        TryDelete(Resolve(storageKey));
        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default) =>
        Task.FromResult(File.Exists(Resolve(storageKey)));

    /// <summary>Chemin absolu d'une clé ; refuse toute clé qui sortirait du dossier racine.</summary>
    internal string Resolve(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey) || Path.IsPathRooted(storageKey) || storageKey.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("Clé de stockage invalide.", nameof(storageKey));
        }

        var full = Path.GetFullPath(Path.Combine(_root, storageKey.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_root, StringComparison.Ordinal))
        {
            throw new ArgumentException("Clé de stockage invalide.", nameof(storageKey));
        }

        return full;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // Fichier verrouillé : il sera écrasé ou ignoré.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
