using Barthazon.Commun.Composants;
using Barthazon.Commun.Contrats;
using Barthazon.Commun.Donnees;
using Barthazon.Commun.Historique;
using Barthazon.Commun.Modules;
using Barthazon.Commun.Parametres;
using Barthazon.Commun.Securite;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Barthazon.Commun;

public static class CommunExtensions
{
    /// <summary>Socle commun : base, connexion, rôles, historique, paramètres, mise en page.</summary>
    public static IServiceCollection AjouterCommun(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environnement)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var options = configuration.GetSection(OptionsBarthazon.Section).Get<OptionsBarthazon>() ?? new OptionsBarthazon();
        options.CheminDonnees = OptionsBarthazon.ResoudreDossier(options.DossierDonnees, environnement);
        services.AddSingleton(options);
        services.AddSingleton(new ChaineConnexion(BaseDeDonneesExtensions.CalculerChaine(configuration, options)));
        services.AjouterContexte<CommunDbContext>(CommunDbContext.TableHistorique, ordre: 0);
        services.AddSingleton<InitialiseurBase>();

        services.AjouterAuthentification(options);
        services.AddScoped<ServiceUtilisateurs>();
        services.AddScoped<ServiceHistorique>();
        services.AddScoped<ServiceParametres>();
        services.AddScoped<ServiceTheme>();

        services.AddSingleton<CatalogueModules>();
        services.AddSingleton<ServiceBascule>();
        services.AddSingleton<SignauxApplication>();
        services.AddScoped<ServiceToast>();
        services.AddScoped<ServiceConfirmation>();

        // Implémentations vides : remplacées par chaque application chargée.
        services.TryAddScoped<IAnnuaireLecture, AnnuaireLectureVide>();
        services.TryAddScoped<IStockLecture, StockLectureVide>();
        return services;
    }

    /// <summary>Remplace l'implémentation d'un contrat (quel que soit l'ordre d'enregistrement).</summary>
    public static IServiceCollection FournirContrat<TContrat, TImplementation>(this IServiceCollection services)
        where TContrat : class
        where TImplementation : class, TContrat
    {
        services.RemoveAll<TContrat>();
        services.AddScoped<TContrat, TImplementation>();
        return services;
    }

    /// <summary>Middlewares du socle : à placer après UseAuthentication / UseAuthorization.</summary>
    public static IApplicationBuilder UtiliserCommun(this IApplicationBuilder app)
    {
        app.Use(async (contexte, suite) =>
        {
            // Utilisateur courant pour les services appelés pendant une requête HTTP (téléchargements…).
            contexte.RequestServices.GetRequiredService<UtilisateurCourant>().Definir(contexte.User);
            await suite(contexte);
        });
        return app.UtiliserPremiereUtilisation();
    }

    /// <summary>Logo personnalisé (aussi affiché sur l'écran de connexion, donc sans connexion).</summary>
    private static void MapLogo(IEndpointRouteBuilder points) =>
        points.MapGet("/theme/logo/{nom}", (string nom, HttpContext http, ServiceTheme theme) =>
        {
            if (theme.FichierLogo(nom) is not { } fichier)
            {
                return Results.NotFound();
            }

            // Un SVG ouvert directement ne peut exécuter aucun script ; le nom change à chaque envoi : cache long.
            http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'";
            http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
            return Results.File(fichier.Chemin, fichier.Type);
        }).AllowAnonymous();

    /// <summary>Points d'accès HTTP du socle et de chaque application.</summary>
    public static IEndpointRouteBuilder MapCommun(this IEndpointRouteBuilder points)
    {
        ArgumentNullException.ThrowIfNull(points);
        points.MapAuthentification();
        MapLogo(points);
        foreach (var module in points.ServiceProvider.GetRequiredService<CatalogueModules>().Modules)
        {
            module.PointsAcces?.Invoke(points);
        }

        return points;
    }
}
