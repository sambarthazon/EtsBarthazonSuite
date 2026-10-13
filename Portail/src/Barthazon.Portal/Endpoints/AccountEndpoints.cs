using Barthazon.Portal.Hosting;
using Barthazon.Referential.Core.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;

namespace Barthazon.Portal.Endpoints;

internal static class AccountEndpoints
{
    public static void MapAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost(PortalAuthentication.LogoutPath, async (HttpContext context, [FromForm] string? returnUrl) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.LocalRedirect(SafeReturnUrl(returnUrl));
        }).AllowAnonymous();
    }

    /// <summary>Uniquement des adresses locales (pas de redirection ouverte).</summary>
    public static string SafeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/";
        }

        var url = returnUrl.StartsWith('/') ? returnUrl : "/" + returnUrl;
        return url.StartsWith("//", StringComparison.Ordinal) || url.StartsWith("/\\", StringComparison.Ordinal) ? "/" : url;
    }

    /// <summary>Au premier démarrage (aucun compte), toute page mène à la création du compte administrateur.</summary>
    public static IApplicationBuilder UseFirstRunRedirect(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var state = context.RequestServices.GetRequiredService<FirstRunState>();
            var path = context.Request.Path;
            if (!state.HasUsers
                && HttpMethods.IsGet(context.Request.Method)
                && !path.StartsWithSegments(PortalAuthentication.SetupPath)
                && !path.StartsWithSegments("/_framework") && !path.StartsWithSegments("/_content") && !path.StartsWithSegments("/_blazor")
                && !path.StartsWithSegments("/health") && !Path.HasExtension(path.Value))
            {
                var users = context.RequestServices.GetRequiredService<IUserService>();
                state.HasUsers = await users.AnyUserAsync(context.RequestAborted);
                if (!state.HasUsers)
                {
                    context.Response.Redirect(PortalAuthentication.SetupPath);
                    return;
                }
            }

            await next();
        });
}
