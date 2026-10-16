namespace Barthazon.Shared.Kernel.Features;

/// <summary>Fonctions à venir, activables par configuration (section « Features »).</summary>
public static class FeatureNames
{
    public const string Stock = "Stock";
    public const string Deliveries = "Deliveries";
    public const string Orders = "Orders";
    public const string TimeTracking = "TimeTracking";
}

/// <summary>État d'activation des fonctions à venir.</summary>
public interface IFeatureFlags
{
    bool IsEnabled(string featureName);
}
