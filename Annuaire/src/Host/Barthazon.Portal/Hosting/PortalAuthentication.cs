using System.Security.Claims;
using Barthazon.Referential.Core.Services;
using Barthazon.Shared.Infrastructure.Security;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;

namespace Barthazon.Portal.Hosting;

/// <summary>Authentification par cookie et comptes locaux.</summary>
internal static class PortalAuthentication
{
    public const string LoginPath = "/connexion";
    public const string SetupPath = "/premier-demarrage";
    public const string LogoutPath = "/compte/deconnexion";

    public static IServiceCollection AddPortalAuthentication(this IServiceCollection services, SecurityOptions security, string keysDirectory)
    {
        // Clés de signature des cookies conservées dans un dossier connu : la session survit aux redémarrages et mises à jour.
        Directory.CreateDirectory(keysDirectory);
        var dataProtection = services.AddDataProtection()
            .SetApplicationName("Barthazon")
            .PersistKeysToFileSystem(new DirectoryInfo(keysDirectory));
        if (OperatingSystem.IsWindows())
        {
            dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
        }

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = LoginPath;
                options.LogoutPath = LogoutPath;
                options.AccessDeniedPath = "/acces-refuse";
                options.ReturnUrlParameter = "returnUrl";
                options.ExpireTimeSpan = TimeSpan.FromDays(security.SessionDays);
                options.SlidingExpiration = true;
                options.Cookie.Name = "bz-auth";
                options.Cookie.HttpOnly = true;
                options.Cookie.SameSite = SameSiteMode.Lax;
                options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.Events.OnValidatePrincipal = ValidatePrincipalAsync;
            });

        services.AddAuthorization(options =>
        {
            if (!security.AllowAnonymousRead)
            {
                // Consultation réservée aux utilisateurs connectés.
                options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();
            }
        });
        services.AddCascadingAuthenticationState();
        services.AddScoped<CurrentUserAccessor>();
        services.AddScoped<Barthazon.Shared.Kernel.Security.ICurrentUser>(sp => sp.GetRequiredService<CurrentUserAccessor>());
        services.AddScoped<Microsoft.AspNetCore.Components.Server.Circuits.CircuitHandler, UserCircuitHandler>();
        services.AddSingleton<FirstRunState>();
        return services;
    }

    public static ClaimsPrincipal CreatePrincipal(AuthenticatedUser user)
    {
        ArgumentNullException.ThrowIfNull(user);
        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                new Claim(ClaimTypes.Name, user.UserName),
                new Claim(BarthazonClaims.DisplayName, user.DisplayName),
                new Claim(ClaimTypes.Role, user.Role.ToString()),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identity);
    }

    public static Task SignInAsync(HttpContext context, AuthenticatedUser user, SecurityOptions security) =>
        context.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            CreatePrincipal(user),
            new AuthenticationProperties { IsPersistent = true, ExpiresUtc = DateTimeOffset.UtcNow.AddDays(security.SessionDays) });

    /// <summary>Un compte désactivé ou dont le rôle a changé ne garde pas ses anciens droits pendant 30 jours.</summary>
    private static async Task ValidatePrincipalAsync(CookieValidatePrincipalContext context)
    {
        var principal = context.Principal;
        if (principal is null || !Guid.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            context.RejectPrincipal();
            return;
        }

        var users = context.HttpContext.RequestServices.GetRequiredService<IUserService>();
        var user = await users.GetActiveAsync(id, context.HttpContext.RequestAborted);
        if (user is null)
        {
            context.RejectPrincipal();
            await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        if (principal.FindFirstValue(ClaimTypes.Role) != user.Role.ToString() || principal.FindFirstValue(BarthazonClaims.DisplayName) != user.DisplayName)
        {
            context.ReplacePrincipal(CreatePrincipal(user));
            context.ShouldRenew = true;
        }
    }
}

/// <summary>Mémorise qu'au moins un compte existe (évite une requête à chaque page).</summary>
internal sealed class FirstRunState
{
    public bool HasUsers { get; set; }
}
