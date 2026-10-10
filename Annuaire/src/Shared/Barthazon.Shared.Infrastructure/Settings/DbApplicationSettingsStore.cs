using System.Globalization;
using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Shared.Kernel.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Barthazon.Shared.Infrastructure.Settings;

/// <summary>Cache des paramètres (partagé par tous les circuits).</summary>
public sealed class ApplicationSettingsCache
{
    public ApplicationSettings? Current { get; set; }
}

/// <summary>
/// Paramètres de l'application : valeurs de la configuration, éventuellement remplacées
/// depuis l'administration (table sys_settings).
/// </summary>
public sealed class DbApplicationSettingsStore(
    IDbContextFactory<AppDbContext> contextFactory,
    IConfiguration configuration,
    ApplicationSettingsCache cache,
    IAccessGuard accessGuard,
    ICurrentUser currentUser,
    TimeProvider timeProvider) : IApplicationSettingsStore
{
    private const string NameKey = "App.Name";
    private const string PublicBaseUrlKey = "App.PublicBaseUrl";
    private const string ReferencePatternKey = "Catalog.ReferencePattern";

    public async Task<ApplicationSettings> GetAsync(CancellationToken cancellationToken = default)
    {
        if (cache.Current is { } cached)
        {
            return cached;
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var values = await context.Set<SystemSetting>().AsNoTracking().ToDictionaryAsync(s => s.Key, s => s.Value, cancellationToken);
        string Value(string key, string configurationKey, string fallback) =>
            values.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : configuration[configurationKey] ?? fallback;

        var settings = new ApplicationSettings(
            Value(NameKey, "App:Name", "Barthazon"),
            Value(PublicBaseUrlKey, "App:PublicBaseUrl", string.Empty),
            Value(ReferencePatternKey, "Catalog:ReferencePattern", "P-{0:0000}"));
        cache.Current = settings;
        return settings;
    }

    public async Task<Result> SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var allowed = accessGuard.CanAdminister();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        var errors = Validate(settings);
        if (errors.Count > 0)
        {
            return Result.Failure(errors);
        }

        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        var existing = await context.Set<SystemSetting>().ToDictionaryAsync(s => s.Key, cancellationToken);
        var now = timeProvider.GetUtcNow();
        void Set(string key, string value)
        {
            if (!existing.TryGetValue(key, out var setting))
            {
                setting = new SystemSetting { Key = key };
                context.Add(setting);
            }

            setting.Value = value.Trim();
            setting.UpdatedAt = now;
            setting.UpdatedBy = currentUser.UserName;
        }

        Set(NameKey, settings.ApplicationName);
        Set(PublicBaseUrlKey, settings.PublicBaseUrl.TrimEnd('/'));
        Set(ReferencePatternKey, settings.ReferencePattern);
        context.AuditUserName = currentUser.UserName;
        context.AddAuditNote("Settings", Guid.Empty, Kernel.Auditing.AuditActions.Updated,
            System.Text.Json.JsonSerializer.Serialize(settings), null, null);
        await context.SaveChangesAsync(cancellationToken);
        cache.Current = null;
        return Result.Success();
    }

    /// <summary>Contrôle des paramètres (aussi utilisé au démarrage).</summary>
    public static List<Error> Validate(ApplicationSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var errors = new List<Error>();
        if (string.IsNullOrWhiteSpace(settings.ApplicationName))
        {
            errors.Add(Error.Validation("Le nom de l'application est obligatoire.", nameof(settings.ApplicationName)));
        }

        if (!Uri.TryCreate(settings.PublicBaseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add(Error.Validation("L'adresse publique doit être une adresse complète, par exemple http://pc-bureau:5080.", nameof(settings.PublicBaseUrl)));
        }

        if (!IsValidReferencePattern(settings.ReferencePattern))
        {
            errors.Add(Error.Validation("Le modèle de référence doit contenir {0} (ex. P-{0:0000}).", nameof(settings.ReferencePattern)));
        }

        return errors;
    }

    public static bool IsValidReferencePattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern) || !pattern.Contains("{0", StringComparison.Ordinal))
        {
            return false;
        }

        try
        {
            _ = string.Format(CultureInfo.InvariantCulture, pattern, 1);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
