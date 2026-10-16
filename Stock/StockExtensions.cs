using Barthazon.Commun.Contrats;
using Barthazon.Commun.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Stock;

public static class StockExtensions
{
    public const string Cle = "stock";

    /// <summary>Enregistre l'application Stock (pages, services, base).</summary>
    public static IServiceCollection AjouterStock(this IServiceCollection services, IConfiguration configuration)
    {
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
                new("Alertes", "/stock/alertes"),
            ],
            Routes =
            [
                new(TypesObjet.PieceFinie, "/stock/pieces/{id}"),
                new(TypesObjet.Brut, "/stock/bruts/{id}"),
            ],
        });
        return services;
    }
}
