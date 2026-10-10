namespace Barthazon.Shared.Kernel.Security;

/// <summary>Rôles, du moins au plus étendu.</summary>
public enum UserRole
{
    Reader = 0,
    Editor = 1,
    Administrator = 2,
}

/// <summary>
/// Utilisateur courant (connecté ou anonyme). Abstraction utilisée par l'audit et les contrôles de droits,
/// pour pouvoir changer de mécanisme d'authentification sans toucher aux modules.
/// </summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    Guid? UserId { get; }

    /// <summary>Identifiant de connexion (ou null si anonyme).</summary>
    string? UserName { get; }

    string? DisplayName { get; }

    UserRole? Role { get; }
}

/// <summary>Contrôle des droits, appelé par les services applicatifs (pas seulement par l'interface).</summary>
public interface IAccessGuard
{
    /// <summary>Consultation : autorisée à tous si la lecture anonyme est activée, sinon aux connectés.</summary>
    Results.Result CanRead();

    /// <summary>Création / modification : rôle éditeur ou administrateur.</summary>
    Results.Result CanEdit();

    /// <summary>Administration : rôle administrateur.</summary>
    Results.Result CanAdminister();
}
