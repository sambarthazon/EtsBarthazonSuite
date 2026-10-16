using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Claims;
using System.Security.Cryptography;
using Barthazon.Commun.Donnees;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Components.Server.Circuits;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Barthazon.Commun.Securite;

/// <summary>
/// Le PIN est vérifié dans le circuit Blazor (pavé numérique interactif). Le cookie, lui, ne peut être posé
/// que pendant une requête HTTP : le circuit émet un ticket à usage unique, valable 1 minute,
/// échangé contre le cookie par <c>/compte/entrer</c>.
/// </summary>
public sealed class GuichetConnexion(TimeProvider horloge)
{
    private static readonly TimeSpan Validite = TimeSpan.FromMinutes(1);
    private readonly ConcurrentDictionary<string, (UtilisateurConnecte Utilisateur, bool Partage, DateTimeOffset Expire)> _tickets = new();

    public string Emettre(UtilisateurConnecte utilisateur, bool partage)
    {
        foreach (var perime in _tickets.Where(t => t.Value.Expire < horloge.GetUtcNow()).Select(t => t.Key).ToList())
        {
            _tickets.TryRemove(perime, out _);
        }

        var ticket = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        _tickets[ticket] = (utilisateur, partage, horloge.GetUtcNow() + Validite);
        return ticket;
    }

    public bool Consommer(string? ticket, out UtilisateurConnecte? utilisateur, out bool partage)
    {
        utilisateur = null;
        partage = false;
        if (string.IsNullOrEmpty(ticket) || !_tickets.TryRemove(ticket, out var entree) || entree.Expire < horloge.GetUtcNow())
        {
            return false;
        }

        utilisateur = entree.Utilisateur;
        partage = entree.Partage;
        return true;
    }
}

/// <summary>Dernière activité de l'utilisateur dans son circuit Blazor (déconnexion des appareils partagés).</summary>
public sealed class ActiviteCircuit
{
    public DateTimeOffset Derniere { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Avant chaque action dans le circuit : utilisateur courant à jour et activité notée.</summary>
internal sealed class GestionnaireCircuit(
    AuthenticationStateProvider etat,
    UtilisateurCourant courant,
    ActiviteCircuit activite,
    TimeProvider horloge) : CircuitHandler, IDisposable
{
    public override async Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        etat.AuthenticationStateChanged += SurChangement;
        courant.Definir((await etat.GetAuthenticationStateAsync()).User);
    }

    public void Dispose() => etat.AuthenticationStateChanged -= SurChangement;

    private async void SurChangement(Task<AuthenticationState> tache)
    {
        try
        {
            courant.Definir((await tache).User);
        }
        catch (InvalidOperationException)
        {
            // Circuit en cours de fermeture : rien à mettre à jour.
        }
    }

    public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(Func<CircuitInboundActivityContext, Task> next) =>
        async contexte =>
        {
            var authentification = await etat.GetAuthenticationStateAsync();
            courant.Definir(authentification.User);
            activite.Derniere = horloge.GetUtcNow();
            await next(contexte);
        };
}

/// <summary>
/// Vérifie chaque minute que la session est toujours valable : compte actif, durée maximale,
/// inactivité sur un appareil partagé. Sinon la page renvoie vers l'écran de connexion.
/// </summary>
internal sealed class EtatAuthentificationRevalide(
    ILoggerFactory journaux,
    IServiceScopeFactory portees,
    ActiviteCircuit activite,
    OptionsBarthazon options,
    TimeProvider horloge) : RevalidatingServerAuthenticationStateProvider(journaux)
{
    protected override TimeSpan RevalidationInterval => TimeSpan.FromMinutes(1);

    protected override async Task<bool> ValidateAuthenticationStateAsync(AuthenticationState authenticationState, CancellationToken cancellationToken)
    {
        var principal = authenticationState.User;
        if (principal.Identity?.IsAuthenticated != true)
        {
            return true;
        }

        var maintenant = horloge.GetUtcNow();
        if (Authentification.FinDeSession(principal) < maintenant)
        {
            return false;
        }

        if (principal.FindFirstValue(Revendications.Partage) == "1"
            && maintenant - activite.Derniere > TimeSpan.FromMinutes(options.DureeInactiviteMinutes))
        {
            return false;
        }

        if (!int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            return false;
        }

        await using var portee = portees.CreateAsyncScope();
        var utilisateur = await portee.ServiceProvider.GetRequiredService<ServiceUtilisateurs>().ObtenirActif(id);
        return utilisateur is not null && utilisateur.Role.ToString() == principal.FindFirstValue(ClaimTypes.Role);
    }
}

/// <summary>Mémorise qu'au moins un utilisateur existe (évite une requête à chaque page).</summary>
internal sealed class EtatPremiereUtilisation
{
    public bool UtilisateursExistent { get; set; }
}

public static class Authentification
{
    public const string CheminConnexion = "/connexion";
    public const string CheminPremiereUtilisation = "/premiere-utilisation";
    public const string CheminEntrer = "/compte/entrer";
    public const string CheminDeconnexion = "/compte/deconnexion";
    public const string CheminActivite = "/compte/actif";

    internal static IServiceCollection AjouterAuthentification(this IServiceCollection services, OptionsBarthazon options)
    {
        // Clés de signature des cookies conservées avec les données : la connexion survit aux redémarrages.
        services.AddDataProtection()
            .SetApplicationName("Barthazon")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(options.CheminDonnees, "cles")));

        services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(cookie =>
            {
                cookie.LoginPath = CheminConnexion;
                cookie.LogoutPath = CheminDeconnexion;
                cookie.AccessDeniedPath = "/";
                cookie.ReturnUrlParameter = "retour";
                cookie.SlidingExpiration = true;
                cookie.Cookie.Name = "bz-session";
                cookie.Cookie.HttpOnly = true;
                cookie.Cookie.SameSite = SameSiteMode.Lax;
                cookie.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                cookie.Events.OnValidatePrincipal = ValiderAsync;
            });
        services.AddAuthorization();
        services.AddCascadingAuthenticationState();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<GuichetConnexion>();
        services.AddSingleton<EtatPremiereUtilisation>();
        services.AddScoped<ActiviteCircuit>();
        services.AddScoped<UtilisateurCourant>();
        services.AddScoped<IUtilisateurCourant>(sp => sp.GetRequiredService<UtilisateurCourant>());
        services.AddScoped<AuthenticationStateProvider, EtatAuthentificationRevalide>();
        services.AddScoped<CircuitHandler, GestionnaireCircuit>();
        return services;
    }

    public static ClaimsPrincipal CreerPrincipal(UtilisateurConnecte utilisateur, bool partage, DateTimeOffset fin)
    {
        ArgumentNullException.ThrowIfNull(utilisateur);
        var identite = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, utilisateur.Id.ToString(CultureInfo.InvariantCulture)),
                new Claim(ClaimTypes.Name, utilisateur.Prenom),
                new Claim(Revendications.Initiales, utilisateur.Initiales),
                new Claim(ClaimTypes.Role, utilisateur.Role.ToString()),
                new Claim(Revendications.Partage, partage ? "1" : "0"),
                new Claim(Revendications.Fin, fin.UtcTicks.ToString(CultureInfo.InvariantCulture)),
            ],
            CookieAuthenticationDefaults.AuthenticationScheme);
        return new ClaimsPrincipal(identite);
    }

    internal static DateTimeOffset FinDeSession(ClaimsPrincipal principal) =>
        long.TryParse(principal.FindFirstValue(Revendications.Fin), out var ticks)
            ? new DateTimeOffset(ticks, TimeSpan.Zero)
            : DateTimeOffset.MinValue;

    /// <summary>Un compte désactivé, ou dont le rôle a changé, ne garde pas ses anciens droits.</summary>
    private static async Task ValiderAsync(CookieValidatePrincipalContext contexte)
    {
        var principal = contexte.Principal;
        var horloge = contexte.HttpContext.RequestServices.GetRequiredService<TimeProvider>();
        if (principal is null
            || FinDeSession(principal) < horloge.GetUtcNow()
            || !int.TryParse(principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id))
        {
            contexte.RejectPrincipal();
            await contexte.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        var utilisateur = await contexte.HttpContext.RequestServices.GetRequiredService<ServiceUtilisateurs>().ObtenirActif(id);
        if (utilisateur is null)
        {
            contexte.RejectPrincipal();
            await contexte.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return;
        }

        if (principal.FindFirstValue(ClaimTypes.Role) != utilisateur.Role.ToString())
        {
            contexte.ReplacePrincipal(CreerPrincipal(utilisateur, principal.FindFirstValue(Revendications.Partage) == "1", FinDeSession(principal)));
            contexte.ShouldRenew = true;
        }
    }

    /// <summary>Points d'accès HTTP de la connexion : échange du ticket, déconnexion, maintien de session.</summary>
    internal static void MapAuthentification(this IEndpointRouteBuilder points)
    {
        points.MapGet(CheminEntrer, async (HttpContext http, string? ticket, string? retour, GuichetConnexion guichet, OptionsBarthazon options, TimeProvider horloge) =>
        {
            if (!guichet.Consommer(ticket, out var utilisateur, out var partage) || utilisateur is null)
            {
                return Results.LocalRedirect(CheminConnexion);
            }

            var maintenant = horloge.GetUtcNow();
            var fin = maintenant.AddHours(options.DureeSessionHeures);

            // Appareil partagé : cookie de session (fermé avec le navigateur), glissant sur la durée d'inactivité.
            var proprietes = new AuthenticationProperties
            {
                IsPersistent = !partage,
                IssuedUtc = maintenant,
                ExpiresUtc = partage ? maintenant.AddMinutes(options.DureeInactiviteMinutes) : fin,
            };
            await http.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, CreerPrincipal(utilisateur, partage, fin), proprietes);
            return Results.LocalRedirect(RetourSur(retour));
        }).AllowAnonymous();

        points.MapGet(CheminDeconnexion, async (HttpContext http, string? motif, string? retour) =>
        {
            await http.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            var parametres = new List<string>();
            if (motif == "inactivite")
            {
                parametres.Add("motif=inactivite");
            }

            if (RetourSur(retour) != "/")
            {
                parametres.Add("retour=" + Uri.EscapeDataString(RetourSur(retour)));
            }

            return Results.LocalRedirect(parametres.Count == 0 ? CheminConnexion : CheminConnexion + "?" + string.Join('&', parametres));
        }).AllowAnonymous();

        // Appelé par le navigateur quand l'utilisateur est actif : prolonge le cookie glissant.
        points.MapGet(CheminActivite, (HttpContext http) =>
            http.User.Identity?.IsAuthenticated == true ? Results.NoContent() : Results.Unauthorized()).AllowAnonymous();
    }

    /// <summary>Uniquement des adresses locales (pas de redirection ouverte).</summary>
    public static string RetourSur(string? retour)
    {
        if (string.IsNullOrWhiteSpace(retour) || !retour.StartsWith('/') || retour.StartsWith("//", StringComparison.Ordinal)
            || retour.StartsWith("/\\", StringComparison.Ordinal) || retour.StartsWith(CheminConnexion, StringComparison.OrdinalIgnoreCase))
        {
            return "/";
        }

        return retour;
    }

    /// <summary>Au premier lancement (aucun utilisateur), toute page mène à la création du premier administrateur.</summary>
    internal static IApplicationBuilder UtiliserPremiereUtilisation(this IApplicationBuilder app) =>
        app.Use(async (contexte, suite) =>
        {
            var etat = contexte.RequestServices.GetRequiredService<EtatPremiereUtilisation>();
            var chemin = contexte.Request.Path;
            if (!etat.UtilisateursExistent
                && HttpMethods.IsGet(contexte.Request.Method)
                && !chemin.StartsWithSegments(CheminPremiereUtilisation)
                && !chemin.StartsWithSegments("/_framework") && !chemin.StartsWithSegments("/_content")
                && !chemin.StartsWithSegments("/_blazor") && !Path.HasExtension(chemin.Value))
            {
                etat.UtilisateursExistent = await contexte.RequestServices.GetRequiredService<ServiceUtilisateurs>().ExisteUtilisateur();
                if (!etat.UtilisateursExistent)
                {
                    contexte.Response.Redirect(CheminPremiereUtilisation);
                    return;
                }
            }

            await suite(contexte);
        });
}
