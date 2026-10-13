using System.Globalization;
using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Infrastructure.Settings;
using Barthazon.Shared.Kernel.Resources;
using Barthazon.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Barthazon.Shared.Infrastructure.Backup;

/// <summary>État des sauvegardes (affiché dans l'administration).</summary>
public sealed record BackupStatus(DateTimeOffset? LastSuccessAt, string? LastFile, DateTimeOffset? LastAttemptAt, string? LastError)
{
    /// <summary>Alerte si la dernière sauvegarde réussie a plus de 48 h (ou n'a jamais eu lieu).</summary>
    public bool IsOverdue(DateTimeOffset now) => LastSuccessAt is null || now - LastSuccessAt.Value > TimeSpan.FromHours(48);
}

/// <summary>Fichier de sauvegarde présent dans le dossier.</summary>
public sealed record BackupFile(string Name, long SizeBytes, DateTimeOffset CreatedAt);

/// <summary>
/// Sauvegarde cohérente de la base SQLite (VACUUM INTO, utilisable pendant que l'application tourne)
/// vers le dossier configuré, avec rétention.
/// </summary>
public sealed partial class BackupService(
    IDbContextFactory<SocleDbContext> contextFactory,
    DatabaseOptions database,
    BackupOptions options,
    TimeProvider timeProvider,
    ILogger<BackupService> logger)
{
    private const string FilePrefix = "barthazon-";
    private const string LastSuccessKey = "Backup.LastSuccessAt";
    private const string LastFileKey = "Backup.LastFile";
    private const string LastAttemptKey = "Backup.LastAttemptAt";
    private const string LastErrorKey = "Backup.LastError";
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task<Result<BackupFile>> RunAsync(CancellationToken cancellationToken = default)
    {
        if (!database.IsSqlite)
        {
            return Error.Validation(KernelTexts.BackupNotSupported);
        }

        await Gate.WaitAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        try
        {
            Directory.CreateDirectory(options.Directory);
            var name = $"{FilePrefix}{timeProvider.GetLocalNow():yyyyMMdd-HHmmss}.db";
            var path = Path.Combine(options.Directory, name);
            await using (var context = await contextFactory.CreateDbContextAsync(cancellationToken))
            {
                await context.Database.ExecuteSqlRawAsync("VACUUM INTO {0}", [path], cancellationToken);
            }

            ApplyRetention();
            await SaveStatusAsync(new Dictionary<string, string?>
            {
                [LastSuccessKey] = now.ToString("O", CultureInfo.InvariantCulture),
                [LastFileKey] = name,
                [LastAttemptKey] = now.ToString("O", CultureInfo.InvariantCulture),
                [LastErrorKey] = null,
            }, cancellationToken);
            LogBackupDone(logger, path);
            var info = new FileInfo(path);
            return new BackupFile(name, info.Length, now);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            LogBackupFailed(logger, ex);
            await SaveStatusAsync(new Dictionary<string, string?>
            {
                [LastAttemptKey] = now.ToString("O", CultureInfo.InvariantCulture),
                [LastErrorKey] = ex.Message,
            }, cancellationToken);
            return Error.Validation(string.Format(CultureInfo.CurrentCulture, KernelTexts.BackupFailed, ex.Message));
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<BackupStatus> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var values = await context.Set<SystemSetting>().AsNoTracking()
            .Where(s => s.Key.StartsWith("Backup."))
            .ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        static DateTimeOffset? Date(string? value) =>
            DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var d) ? d : null;
        return new BackupStatus(
            Date(values.GetValueOrDefault(LastSuccessKey)),
            values.GetValueOrDefault(LastFileKey),
            Date(values.GetValueOrDefault(LastAttemptKey)),
            values.GetValueOrDefault(LastErrorKey));
    }

    public IReadOnlyList<BackupFile> ListFiles()
    {
        if (!Directory.Exists(options.Directory))
        {
            return [];
        }

        return new DirectoryInfo(options.Directory)
            .GetFiles(FilePrefix + "*.db")
            .OrderByDescending(f => f.LastWriteTimeUtc)
            .Select(f => new BackupFile(f.Name, f.Length, new DateTimeOffset(f.LastWriteTimeUtc, TimeSpan.Zero)))
            .ToList();
    }

    private void ApplyRetention()
    {
        var limit = timeProvider.GetUtcNow().UtcDateTime.AddDays(-options.RetentionDays);
        foreach (var file in new DirectoryInfo(options.Directory).GetFiles(FilePrefix + "*.db").Where(f => f.LastWriteTimeUtc < limit))
        {
            try
            {
                file.Delete();
            }
            catch (IOException)
            {
                // Fichier verrouillé : retenté à la prochaine sauvegarde.
            }
        }
    }

    private async Task SaveStatusAsync(Dictionary<string, string?> values, CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.Set<SystemSetting>().Where(s => values.Keys.Contains(s.Key)).ToDictionaryAsync(s => s.Key, cancellationToken);
        foreach (var (key, value) in values)
        {
            if (!existing.TryGetValue(key, out var setting))
            {
                setting = new SystemSetting { Key = key };
                context.Add(setting);
            }

            setting.Value = value;
            setting.UpdatedAt = timeProvider.GetUtcNow();
            setting.UpdatedBy = "sauvegarde";
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Sauvegarde de la base effectuée : {Path}")]
    private static partial void LogBackupDone(ILogger logger, string path);

    [LoggerMessage(Level = LogLevel.Error, Message = "Échec de la sauvegarde de la base")]
    private static partial void LogBackupFailed(ILogger logger, Exception exception);
}
