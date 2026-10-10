namespace Barthazon.Shared.Kernel.Settings;

/// <summary>Paramètres modifiables depuis l'administration (surchargent la configuration).</summary>
public sealed record ApplicationSettings(string ApplicationName, string PublicBaseUrl, string ReferencePattern);

/// <summary>Lecture / écriture des paramètres de l'application.</summary>
public interface IApplicationSettingsStore
{
    Task<ApplicationSettings> GetAsync(CancellationToken cancellationToken = default);

    Task<Results.Result> SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default);
}
