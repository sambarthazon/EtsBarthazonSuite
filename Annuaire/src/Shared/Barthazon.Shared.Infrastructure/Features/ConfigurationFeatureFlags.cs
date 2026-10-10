using Barthazon.Shared.Kernel.Features;
using Microsoft.Extensions.Configuration;

namespace Barthazon.Shared.Infrastructure.Features;

/// <summary>Fonctions à venir activées par la configuration (section « Features », toutes à false en V1).</summary>
public sealed class ConfigurationFeatureFlags(IConfiguration configuration) : IFeatureFlags
{
    public bool IsEnabled(string featureName) =>
        configuration.GetValue($"Features:{featureName}", defaultValue: false);
}
