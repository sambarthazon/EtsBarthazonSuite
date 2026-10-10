using System.Globalization;
using Barthazon.Shared.Infrastructure.Backup;
using Barthazon.Shared.Infrastructure.Files;
using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Infrastructure.Security;
using Barthazon.Shared.Infrastructure.Settings;
using Microsoft.Data.Sqlite;
using Barthazon.Shared.Infrastructure.Resources;
using Microsoft.Extensions.Configuration;

namespace Barthazon.Shared.Infrastructure.Hosting;

/// <summary>Résultat de la validation : erreurs bloquantes et avertissements (affichés dans l'administration).</summary>
public sealed record ConfigurationReport(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Validation de la configuration au démarrage : en cas d'erreur (dossier inaccessible, chemin invalide…),
/// l'application s'arrête avec un message clair.
/// </summary>
public static class ConfigurationValidator
{
    public static ConfigurationReport Validate(IConfiguration configuration, string contentRoot)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var errors = new List<string>();
        var warnings = new List<string>();

        var database = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        if (!database.IsSqlite && !database.IsPostgres)
        {
            errors.Add(Format(InfrastructureTexts.ProviderUnknown, database.Provider));
        }
        else if (string.IsNullOrWhiteSpace(database.ConnectionString))
        {
            errors.Add(InfrastructureTexts.ConnectionStringEmpty);
        }
        else if (database.IsSqlite)
        {
            try
            {
                var dataSource = new SqliteConnectionStringBuilder(database.ConnectionString).DataSource;
                if (string.IsNullOrWhiteSpace(dataSource))
                {
                    errors.Add(InfrastructureTexts.DataSourceMissing);
                }
                else if (dataSource != ":memory:")
                {
                    var path = Path.GetFullPath(dataSource, contentRoot);
                    CheckWritableDirectory(Path.GetDirectoryName(path)!, InfrastructureTexts.DirectoryDatabase, errors);
                    if (AppDbContextFactory.IsNetworkPath(path))
                    {
                        warnings.Add(InfrastructureTexts.DatabaseOnNetworkShare);
                    }
                }
            }
            catch (ArgumentException ex)
            {
                errors.Add(Format(InfrastructureTexts.ConnectionStringInvalid, ex.Message));
            }
        }

        var files = configuration.GetSection(FileStorageOptions.SectionName).Get<FileStorageOptions>() ?? new FileStorageOptions();
        if (string.IsNullOrWhiteSpace(files.RootPath))
        {
            errors.Add(InfrastructureTexts.FilesRootEmpty);
        }
        else
        {
            CheckWritableDirectory(Path.GetFullPath(files.RootPath, contentRoot), InfrastructureTexts.DirectoryFiles, errors);
        }

        if (files.MaxFileSizeMb <= 0)
        {
            errors.Add(InfrastructureTexts.MaxFileSizeInvalid);
        }

        var backup = configuration.GetSection(BackupOptions.SectionName).Get<BackupOptions>() ?? new BackupOptions();
        if (backup.Enabled)
        {
            if (backup.DailyTime is null)
            {
                errors.Add(Format(InfrastructureTexts.BackupTimeInvalid, backup.DailyAt));
            }

            if (backup.RetentionDays <= 0)
            {
                errors.Add(InfrastructureTexts.RetentionInvalid);
            }

            if (string.IsNullOrWhiteSpace(backup.Directory))
            {
                errors.Add(InfrastructureTexts.BackupDirectoryEmpty);
            }
            else
            {
                // Le partage réseau peut être momentanément indisponible : avertissement seulement.
                var backupErrors = new List<string>();
                CheckWritableDirectory(Path.GetFullPath(backup.Directory, contentRoot), InfrastructureTexts.DirectoryBackups, backupErrors);
                warnings.AddRange(backupErrors);
            }
        }

        var security = configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();
        if (security.SessionDays <= 0)
        {
            errors.Add(InfrastructureTexts.SessionDaysInvalid);
        }

        var publicUrl = configuration["App:PublicBaseUrl"];
        if (!Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add(Format(InfrastructureTexts.PublicUrlInvalid, publicUrl));
        }

        var culture = configuration["App:Culture"] ?? "fr-FR";
        try
        {
            _ = CultureInfo.GetCultureInfo(culture);
        }
        catch (CultureNotFoundException)
        {
            errors.Add(Format(InfrastructureTexts.CultureUnknown, culture));
        }

        var pattern = configuration["Catalog:ReferencePattern"] ?? "P-{0:0000}";
        if (!DbApplicationSettingsStore.IsValidReferencePattern(pattern))
        {
            errors.Add(Format(InfrastructureTexts.ReferencePatternInvalid, pattern));
        }

        return new ConfigurationReport(errors, warnings);
    }

    private static string Format(string format, params object?[] args) => string.Format(CultureInfo.CurrentCulture, format, args);

    private static void CheckWritableDirectory(string directory, string description, List<string> errors)
    {
        try
        {
            Directory.CreateDirectory(directory);
            var probe = Path.Combine(directory, $".barthazon-write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            errors.Add(Format(InfrastructureTexts.DirectoryNotWritable, description, directory, ex.Message));
        }
    }
}

/// <summary>Avertissements de configuration conservés pour la page d'administration.</summary>
public sealed class ConfigurationWarnings(IReadOnlyList<string> warnings)
{
    public IReadOnlyList<string> Items { get; } = warnings;
}
