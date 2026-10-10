using System.Globalization;
using System.Reflection;
using Barthazon.Shared.Infrastructure.Backup;
using Barthazon.Shared.Infrastructure.DataTransfer;
using Barthazon.Shared.Infrastructure.Files;
using Barthazon.Shared.Infrastructure.Hosting;
using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Infrastructure.Security;
using Barthazon.Shared.Kernel.Auditing;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Resources;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;

namespace Barthazon.Shared.Infrastructure.Administration;

/// <summary>Espace disque d'un emplacement.</summary>
public sealed record DiskSpace(string Path, long? FreeBytes, long? TotalBytes);

/// <summary>État du système (page d'administration).</summary>
public sealed record SystemStatus(
    string Version,
    string DatabaseProvider,
    string? DatabasePath,
    string FilesPath,
    string BackupDirectory,
    bool AllowAnonymousRead,
    BackupStatus Backup,
    bool BackupOverdue,
    IReadOnlyList<DiskSpace> Disks,
    IReadOnlyList<string> Warnings);

/// <summary>Fichier produit pour l'administrateur.</summary>
public sealed record AdminFile(string FileName, string ContentType, byte[] Content);

/// <summary>Administration : journal global, état du système, sauvegardes, export / import complet. Réservé aux administrateurs.</summary>
public sealed class AdministrationService(
    IAccessGuard accessGuard,
    IAuditReader auditReader,
    BackupService backups,
    DataTransferService dataTransfer,
    DatabaseOptions database,
    FileStorageOptions files,
    BackupOptions backupOptions,
    SecurityOptions security,
    ConfigurationWarnings configurationWarnings,
    TimeProvider timeProvider)
{
    public async Task<Result<PagedResult<AuditEntry>>> QueryAuditAsync(AuditQuery query, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanAdminister();
        return allowed.IsFailure
            ? Result<PagedResult<AuditEntry>>.Failure(allowed.Errors)
            : await auditReader.QueryAsync(query, cancellationToken);
    }

    public async Task<Result<SystemStatus>> GetStatusAsync(CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanAdminister();
        if (allowed.IsFailure)
        {
            return Result<SystemStatus>.Failure(allowed.Errors);
        }

        var dbPath = AppDbContextFactory.GetSqliteFilePath(database);
        var backup = await backups.GetStatusAsync(cancellationToken);
        var overdue = backupOptions.Enabled && database.IsSqlite && backup.IsOverdue(timeProvider.GetUtcNow());
        var warnings = configurationWarnings.Items.ToList();
        if (!backupOptions.Enabled)
        {
            warnings.Add(Resources.InfrastructureTexts.BackupDisabled);
        }

        var version = typeof(AdministrationService).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0] ?? "?";
        var disks = new[] { dbPath is null ? null : Path.GetDirectoryName(dbPath), files.RootPath, backupOptions.Directory }
            .OfType<string>()
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(Disk)
            .ToList();
        return new SystemStatus(version, database.Provider, dbPath, files.RootPath, backupOptions.Directory, security.AllowAnonymousRead, backup, overdue, disks, warnings);
    }

    public async Task<Result<BackupFile>> BackupNowAsync(CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanAdminister();
        return allowed.IsFailure ? Result<BackupFile>.Failure(allowed.Errors) : await backups.RunAsync(cancellationToken);
    }

    public Result<IReadOnlyList<BackupFile>> ListBackups()
    {
        var allowed = accessGuard.CanAdminister();
        return allowed.IsFailure ? Result<IReadOnlyList<BackupFile>>.Failure(allowed.Errors) : Result.Success(backups.ListFiles());
    }

    public async Task<Result<AdminFile>> ExportAllAsync(CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanAdminister();
        if (allowed.IsFailure)
        {
            return Result<AdminFile>.Failure(allowed.Errors);
        }

        var content = await dataTransfer.ExportAsync(cancellationToken);
        var name = string.Format(CultureInfo.InvariantCulture, "barthazon-export-{0:yyyyMMdd-HHmmss}.json", timeProvider.GetLocalNow());
        return new AdminFile(name, "application/json", content);
    }

    public async Task<Result<DataTransferReport>> ImportAllAsync(Stream content, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanAdminister();
        if (allowed.IsFailure)
        {
            return Result<DataTransferReport>.Failure(allowed.Errors);
        }

        try
        {
            return await dataTransfer.ImportAsync(content, cancellationToken);
        }
        catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
        {
            return Error.Validation(KernelTexts.ImportNotAFile);
        }
        catch (Microsoft.EntityFrameworkCore.DbUpdateException ex)
        {
            return Error.Validation(string.Format(CultureInfo.CurrentCulture, KernelTexts.ImportFailed, ex.InnerException?.Message ?? ex.Message));
        }
    }

    private static DiskSpace Disk(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root) || root.StartsWith(@"\\", StringComparison.Ordinal))
            {
                return new DiskSpace(path, null, null);
            }

            var drive = new DriveInfo(root);
            return new DiskSpace(path, drive.AvailableFreeSpace, drive.TotalSize);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
        {
            return new DiskSpace(path, null, null);
        }
    }
}
