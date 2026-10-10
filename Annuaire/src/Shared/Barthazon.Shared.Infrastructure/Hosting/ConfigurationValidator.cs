using System.Globalization;
using Barthazon.Shared.Infrastructure.Backup;
using Barthazon.Shared.Infrastructure.Files;
using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Infrastructure.Security;
using Barthazon.Shared.Infrastructure.Settings;
using Microsoft.Data.Sqlite;
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
            errors.Add($"Database:Provider « {database.Provider} » inconnu (valeurs possibles : Sqlite, Postgres).");
        }
        else if (string.IsNullOrWhiteSpace(database.ConnectionString))
        {
            errors.Add("Database:ConnectionString est vide.");
        }
        else if (database.IsSqlite)
        {
            try
            {
                var dataSource = new SqliteConnectionStringBuilder(database.ConnectionString).DataSource;
                if (string.IsNullOrWhiteSpace(dataSource))
                {
                    errors.Add("Database:ConnectionString doit indiquer le fichier de base (Data Source=...).");
                }
                else if (dataSource != ":memory:")
                {
                    var path = Path.GetFullPath(dataSource, contentRoot);
                    CheckWritableDirectory(Path.GetDirectoryName(path)!, "dossier de la base de données (Database:ConnectionString)", errors);
                    if (AppDbContextFactory.IsNetworkPath(path))
                    {
                        warnings.Add("La base SQLite est sur un partage réseau : risque de corruption. Recommandé : disque local de la machine qui héberge l'application, avec sauvegarde vers le partage. Une seule instance du serveur doit fonctionner (mode de journalisation DELETE).");
                    }
                }
            }
            catch (ArgumentException ex)
            {
                errors.Add($"Database:ConnectionString invalide : {ex.Message}");
            }
        }

        var files = configuration.GetSection(FileStorageOptions.SectionName).Get<FileStorageOptions>() ?? new FileStorageOptions();
        if (string.IsNullOrWhiteSpace(files.RootPath))
        {
            errors.Add("FileStorage:RootPath est vide.");
        }
        else
        {
            CheckWritableDirectory(Path.GetFullPath(files.RootPath, contentRoot), "dossier des fichiers (FileStorage:RootPath)", errors);
        }

        if (files.MaxFileSizeMb <= 0)
        {
            errors.Add("FileStorage:MaxFileSizeMb doit être supérieur à 0.");
        }

        var backup = configuration.GetSection(BackupOptions.SectionName).Get<BackupOptions>() ?? new BackupOptions();
        if (backup.Enabled)
        {
            if (backup.DailyTime is null)
            {
                errors.Add($"Backup:DailyAt « {backup.DailyAt} » invalide (format attendu HH:mm, ex. 12:30).");
            }

            if (backup.RetentionDays <= 0)
            {
                errors.Add("Backup:RetentionDays doit être supérieur à 0.");
            }

            if (string.IsNullOrWhiteSpace(backup.Directory))
            {
                errors.Add("Backup:Directory est vide.");
            }
            else
            {
                // Le partage réseau peut être momentanément indisponible : avertissement seulement.
                var backupErrors = new List<string>();
                CheckWritableDirectory(Path.GetFullPath(backup.Directory, contentRoot), "dossier des sauvegardes (Backup:Directory)", backupErrors);
                warnings.AddRange(backupErrors);
            }
        }

        var security = configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();
        if (security.SessionDays <= 0)
        {
            errors.Add("Security:SessionDays doit être supérieur à 0.");
        }

        var publicUrl = configuration["App:PublicBaseUrl"];
        if (!Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add($"App:PublicBaseUrl « {publicUrl} » invalide (ex. http://pc-bureau:5080). Elle sert aux QR codes.");
        }

        var culture = configuration["App:Culture"] ?? "fr-FR";
        try
        {
            _ = CultureInfo.GetCultureInfo(culture);
        }
        catch (CultureNotFoundException)
        {
            errors.Add($"App:Culture « {culture} » inconnue.");
        }

        var pattern = configuration["Catalog:ReferencePattern"] ?? "P-{0:0000}";
        if (!DbApplicationSettingsStore.IsValidReferencePattern(pattern))
        {
            errors.Add($"Catalog:ReferencePattern « {pattern} » invalide (doit contenir {{0}}, ex. P-{{0:0000}}).");
        }

        return new ConfigurationReport(errors, warnings);
    }

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
            errors.Add($"Le {description} « {directory} » est inaccessible en écriture : {ex.Message}");
        }
    }
}

/// <summary>Avertissements de configuration conservés pour la page d'administration.</summary>
public sealed class ConfigurationWarnings(IReadOnlyList<string> warnings)
{
    public IReadOnlyList<string> Items { get; } = warnings;
}
