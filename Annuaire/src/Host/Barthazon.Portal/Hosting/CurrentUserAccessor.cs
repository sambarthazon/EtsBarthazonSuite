using System.Security.Claims;
using Barthazon.Shared.Kernel.Security;

namespace Barthazon.Portal.Hosting;

/// <summary>Noms des revendications (claims) du cookie d'authentification.</summary>
internal static class BarthazonClaims
{
    public const string DisplayName = "bz:display_name";
}

/// <summary>
/// Utilisateur courant, alimenté par le middleware (requêtes HTTP) et par le gestionnaire de circuit (Blazor Server).
/// </summary>
internal sealed class CurrentUserAccessor : ICurrentUser
{
    private ClaimsPrincipal _principal = new(new ClaimsIdentity());

    public bool IsAuthenticated => _principal.Identity?.IsAuthenticated == true;

    public Guid? UserId => Guid.TryParse(_principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;

    public string? UserName => IsAuthenticated ? _principal.Identity?.Name : null;

    public string? DisplayName => IsAuthenticated ? _principal.FindFirstValue(BarthazonClaims.DisplayName) ?? UserName : null;

    public UserRole? Role => IsAuthenticated && Enum.TryParse<UserRole>(_principal.FindFirstValue(ClaimTypes.Role), out var role) ? role : null;

    public void SetUser(ClaimsPrincipal principal) => _principal = principal;
}
