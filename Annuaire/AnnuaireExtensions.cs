using Barthazon.Annuaire.Donnees;
using Barthazon.Annuaire.Pages.Parametres;
using Barthazon.Annuaire.Services;
using Barthazon.Commun;
using Barthazon.Commun.Contrats;
using Barthazon.Commun.Donnees;
using Barthazon.Commun.Modules;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Annuaire;

public static class AnnuaireExtensions
{
    public const string Cle = "annuaire";

    /// <summary>Enregistre l'application Annuaire : base, services, contrat de lecture, pages et onglets.</summary>
    public static IServiceCollection AjouterAnnuaire(this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterContexte<AnnuaireDbContext>(AnnuaireDbContext.TableHistorique, ordre: 10);
        services.AddScoped<ServiceReferentiels>();
        services.AddScoped<ServiceBruts>();
        services.AddScoped<ServicePiecesFinies>();
        services.AddSingleton<StockagePlans>();
        services.FournirContrat<IAnnuaireLecture, AnnuaireLecture>();
        services.AddScoped<IRechercheGlobale, RechercheAnnuaire>();

        services.AddSingleton(new ModuleApplication
        {
            Cle = Cle,
            Nom = "Annuaire",
            Ordre = 10,
            FeuilleDeStyle = "_content/Barthazon.Annuaire/annuaire.css",
            Assemblage = typeof(AnnuaireExtensions).Assembly,
            Onglets =
            [
                new("Pièces finies", "/annuaire"),
                new("Bruts", "/annuaire/bruts"),
                new("Clients", "/annuaire/clients", Secondaire: true),
                new("Fournisseurs", "/annuaire/fournisseurs", Secondaire: true),
            ],
            Routes =
            [
                new(TypesObjet.PieceFinie, "/annuaire/pieces/{id}"),
                new(TypesObjet.Brut, "/annuaire/bruts/{id}"),
                new(TypesObjet.Client, "/annuaire/clients/{id}"),
                new(TypesObjet.Fournisseur, "/annuaire/fournisseurs/{id}"),
            ],
            RoutesCreation =
            [
                new(TypesObjet.PieceFinie, "/annuaire/pieces/nouveau"),
                new(TypesObjet.Brut, "/annuaire/bruts/nouveau"),
            ],
            Parametres =
            [
                new("matieres", "Matières", typeof(ParametresMatieres)),
                new("postes", "Postes de gamme", typeof(ParametresPostes)),
            ],
            PointsAcces = MapPlans,
        });
        return services;
    }

    /// <summary>Ouverture d'un plan dans le navigateur (PDF, images) ou téléchargement.</summary>
    private static void MapPlans(IEndpointRouteBuilder points) =>
        points.MapGet("/annuaire/plans/{id:int}", async (int id, bool? telecharger, ServicePiecesFinies pieces) =>
        {
            var fichier = await pieces.FichierPlan(id);
            if (fichier is null)
            {
                return Results.NotFound();
            }

            var (chemin, nom, type) = fichier.Value;
            return telecharger == true
                ? Results.File(chemin, type, nom)
                : Results.File(chemin, type, enableRangeProcessing: true);
        }).RequireAuthorization();
}
