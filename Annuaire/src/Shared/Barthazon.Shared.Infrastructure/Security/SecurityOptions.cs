namespace Barthazon.Shared.Infrastructure.Security;

/// <summary>Section de configuration « Security ».</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>La consultation ne demande pas de connexion sur le réseau interne.</summary>
    public bool AllowAnonymousRead { get; set; } = true;

    /// <summary>Durée de la session (cookie), en jours.</summary>
    public int SessionDays { get; set; } = 30;
}
