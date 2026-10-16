using Barthazon.Commun.Contrats;
using Barthazon.Commun.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Annuaire;

public static class AnnuaireExtensions
{
    public const string Cle = "annuaire";

    /// <summary>Enregistre l'application Annuaire (pages, services, base).</summary>
    public static IServiceCollection AjouterAnnuaire(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(new ModuleApplication
        {
            Cle = Cle,
            Nom = "Annuaire",
            Ordre = 10,
            Assemblage = typeof(AnnuaireExtensions).Assembly,
            Onglets =
            [
                new("Pièces finies", "/annuaire"),
                new("Bruts", "/annuaire/bruts"),
            ],
            Routes =
            [
                new(TypesObjet.PieceFinie, "/annuaire/pieces/{id}"),
                new(TypesObjet.Brut, "/annuaire/bruts/{id}"),
            ],
        });
        return services;
    }
}
