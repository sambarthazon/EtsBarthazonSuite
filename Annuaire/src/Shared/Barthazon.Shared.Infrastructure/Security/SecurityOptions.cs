namespace Barthazon.Shared.Infrastructure.Security;

/// <summary>Section de configuration « Security ».</summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";

    /// <summary>La consultation ne demande pas de connexion sur le réseau interne.</summary>
    public bool AllowAnonymousRead { get; set; } = true;

    /// <summary>Durée de la session (cookie), en jours.</summary>
    public int SessionDays { get; set; } = 30;

    /// <summary>
    /// Dossier des clés qui signent les cookies de connexion. Par défaut : dossier « keys » à côté de la base SQLite
    /// (sauvegardé et déplacé avec les données). Sans ces clés, les utilisateurs doivent simplement se reconnecter.
    /// </summary>
    public string? KeysDirectory { get; set; }
}
