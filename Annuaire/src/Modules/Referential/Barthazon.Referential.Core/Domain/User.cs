using Barthazon.Shared.Kernel.Domain;
using Barthazon.Shared.Kernel.Security;

namespace Barthazon.Referential.Core.Domain;

/// <summary>Compte utilisateur local.</summary>
public sealed class User : AuditableEntity
{
    private User()
    {
    }

    public User(string userName, string displayName, UserRole role)
    {
        UserName = NormalizeUserName(userName);
        DisplayName = displayName.Trim();
        Role = role;
        IsActive = true;
    }

    /// <summary>Identifiant de connexion (minuscules).</summary>
    public string UserName { get; private set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Mot de passe haché (outil standard d'ASP.NET Core).</summary>
    public string PasswordHash { get; private set; } = string.Empty;

    public UserRole Role { get; set; }

    public bool IsActive { get; set; }

    public DateTimeOffset? LastLoginAt { get; private set; }

    public void SetPasswordHash(string hash) => PasswordHash = hash;

    public void RecordLogin(DateTimeOffset at) => LastLoginAt = at;

    public static string NormalizeUserName(string userName) => (userName ?? string.Empty).Trim().ToLowerInvariant();
}
