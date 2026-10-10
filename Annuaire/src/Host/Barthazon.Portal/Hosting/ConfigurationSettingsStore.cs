using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Settings;

namespace Barthazon.Portal.Hosting;

/// <summary>Paramètres lus dans la configuration (remplacé par le stockage en base au jalon J8).</summary>
internal sealed class ConfigurationSettingsStore(IConfiguration configuration) : IApplicationSettingsStore
{
    public Task<ApplicationSettings> GetAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(new ApplicationSettings(
            configuration["App:Name"] ?? "Barthazon",
            configuration["App:PublicBaseUrl"] ?? string.Empty,
            configuration["Catalog:ReferencePattern"] ?? "P-{0:0000}"));

    public Task<Result> SaveAsync(ApplicationSettings settings, CancellationToken cancellationToken = default) =>
        Task.FromResult(Result.Success());
}
