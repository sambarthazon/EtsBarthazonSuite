using Barthazon.Commun;
using Barthazon.Commun.Contrats;
using Barthazon.Commun.Donnees;
using Barthazon.Commun.Modules;
using Barthazon.Stock.Donnees;
using Barthazon.Stock.Pages;
using Barthazon.Stock.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Stock;

public static class StockExtensions
{
    public const string Cle = "stock";

    /// <summary>Enregistre l'application Stock : base, services, contrat de lecture, pages, onglets et alertes.</summary>
    public static IServiceCollection AjouterStock(this IServiceCollection services, IConfiguration configuration)
    {
        services.AjouterContexte<StockDbContext>(StockDbContext.TableHistorique, ordre: 20);
        services.AddScoped<ServiceMouvements>();
        services.AddScoped<ServiceStock>();
        services.FournirContrat<IStockLecture, StockLecture>();

        static Task<int> NombreAlertes(IServiceProvider sp) => sp.GetRequiredService<IStockLecture>().NombreAlertes();

        services.AddSingleton(new ModuleApplication
        {
            Cle = Cle,
            Nom = "Stock",
            Ordre = 20,
            Assemblage = typeof(StockExtensions).Assembly,
            Onglets =
            [
                new("Pièces finies", "/stock"),
                new("Bruts", "/stock/bruts"),
                new("Mouvements", "/stock/mouvements"),
                new("Alertes", "/stock/alertes", Pastille: NombreAlertes),
            ],
            Routes =
            [
                new(TypesObjet.PieceFinie, "/stock/pieces/{id}"),
                new(TypesObjet.Brut, "/stock/bruts/{id}"),
            ],
            CompteurAlertes = NombreAlertes,
            CheminAlertes = "/stock/alertes",
            Parametres =
            [
                new("stocks", "Vérifier les stocks", typeof(VerificationStocks)),
            ],
        });
        return services;
    }
}
