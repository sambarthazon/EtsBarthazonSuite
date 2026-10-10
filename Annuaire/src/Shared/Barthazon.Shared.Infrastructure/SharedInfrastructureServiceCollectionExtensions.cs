using Barthazon.Shared.Infrastructure.Administration;
using Barthazon.Shared.Infrastructure.Auditing;
using Barthazon.Shared.Infrastructure.DataTransfer;
using Barthazon.Shared.Infrastructure.Hosting;
using Barthazon.Shared.Infrastructure.Backup;
using Barthazon.Shared.Infrastructure.Features;
using Barthazon.Shared.Infrastructure.Files;
using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Infrastructure.Printing;
using Barthazon.Shared.Infrastructure.Security;
using Barthazon.Shared.Infrastructure.Settings;
using Barthazon.Shared.Kernel.Auditing;
using Barthazon.Shared.Kernel.Features;
using Barthazon.Shared.Kernel.Files;
using Barthazon.Shared.Kernel.Printing;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Shared.Kernel.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Barthazon.Shared.Infrastructure;

public static class SharedInfrastructureServiceCollectionExtensions
{
    /// <summary>
    /// Enregistre le socle technique : base de données (fournisseur choisi par configuration),
    /// modules de persistance, fichiers, audit, paramètres, droits.
    /// </summary>
    public static IServiceCollection AddSharedInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration,
        string contentRoot,
        IReadOnlyList<IModulePersistence> modules)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(modules);

        var database = configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
        if (database.IsSqlite)
        {
            database.ConnectionString = MakeSqlitePathAbsolute(database.ConnectionString, contentRoot);
        }

        var files = configuration.GetSection(FileStorageOptions.SectionName).Get<FileStorageOptions>() ?? new FileStorageOptions();
        files.RootPath = Path.GetFullPath(files.RootPath, contentRoot);
        var backup = configuration.GetSection(BackupOptions.SectionName).Get<BackupOptions>() ?? new BackupOptions();
        backup.Directory = string.IsNullOrWhiteSpace(backup.Directory) ? backup.Directory : Path.GetFullPath(backup.Directory, contentRoot);
        var security = configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();

        services.AddSingleton(database);
        services.AddSingleton(files);
        services.AddSingleton(backup);
        services.AddSingleton(security);
        services.AddSingleton(TimeProvider.System);

        foreach (var module in modules)
        {
            services.AddSingleton(module);
            module.ConfigureServices(services, configuration);
        }

        services.AddSingleton<IDbContextFactory<AppDbContext>>(sp =>
            new AppDbContextFactory(database, sp.GetServices<IModulePersistence>(), sp.GetRequiredService<TimeProvider>()));
        services.AddSingleton<DatabaseInitializer>();
        services.AddSingleton<IAuditReader, AuditReader>();
        services.AddSingleton<IFileStorage>(new FileSystemFileStorage(files));
        services.AddSingleton(new FileStorageLimits(files.MaxFileSizeBytes));
        services.AddSingleton<IFeatureFlags, ConfigurationFeatureFlags>();
        services.AddSingleton<IQrCodeGenerator, QrCodeGenerator>();
        services.AddSingleton<ApplicationSettingsCache>();
        services.AddScoped<IApplicationSettingsStore, DbApplicationSettingsStore>();
        services.AddScoped<IAccessGuard>(sp => new AccessGuard(sp.GetRequiredService<ICurrentUser>(), security.AllowAnonymousRead));

        // Exploitation : sauvegardes, export / import complet, administration, contrôle de santé.
        services.AddSingleton<BackupService>();
        services.AddSingleton<DataTransferService>();
        services.AddScoped<AdministrationService>();
        services.TryAddSingleton(new ConfigurationWarnings([]));
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database")
            .AddCheck<FileStorageHealthCheck>("files");
        return services;
    }

    /// <summary>Un chemin SQLite relatif est résolu par rapport au dossier de l'application.</summary>
    public static string MakeSqlitePathAbsolute(string connectionString, string contentRoot)
    {
        var builder = new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder(connectionString);
        if (!string.IsNullOrWhiteSpace(builder.DataSource) && builder.DataSource != ":memory:" && !Path.IsPathRooted(builder.DataSource))
        {
            builder.DataSource = Path.GetFullPath(builder.DataSource, contentRoot);
        }

        return builder.ToString();
    }
}
