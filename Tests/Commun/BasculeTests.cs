using Barthazon.Commun.Contrats;
using Barthazon.Commun.Modules;

namespace Barthazon.Tests.Commun;

public class BasculeTests
{
    private static readonly ServiceBascule Bascule = new(new CatalogueModules(
    [
        new ModuleApplication
        {
            Cle = "annuaire", Nom = "Annuaire", Ordre = 1, Assemblage = typeof(BasculeTests).Assembly,
            Routes = [new(TypesObjet.PieceFinie, "/annuaire/pieces/{id}"), new(TypesObjet.Brut, "/annuaire/bruts/{id}"), new(TypesObjet.Client, "/annuaire/clients/{id}")],
            RoutesCreation = [new(TypesObjet.Brut, "/annuaire/bruts/nouveau")],
        },
        new ModuleApplication
        {
            Cle = "stock", Nom = "Stock", Ordre = 2, Assemblage = typeof(BasculeTests).Assembly,
            Routes = [new(TypesObjet.PieceFinie, "/stock/pieces/{id}"), new(TypesObjet.Brut, "/stock/bruts/{id}")],
        },
    ]));

    private static ModuleApplication Stock => Module("stock");

    private static ModuleApplication Module(string cle) =>
        new() { Cle = cle, Nom = cle, Assemblage = typeof(BasculeTests).Assembly, Routes = cle == "stock" ? [new(TypesObjet.PieceFinie, "/stock/pieces/{id}"), new(TypesObjet.Brut, "/stock/bruts/{id}")] : [] };

    [Theory]
    [InlineData("/annuaire/pieces/12", "/stock/pieces/12")]
    [InlineData("/annuaire/pieces/12/gamme?x=1", "/stock/pieces/12")]
    [InlineData("/annuaire/bruts/3", "/stock/bruts/3")]
    [InlineData("/annuaire/clients/4", "/stock")]
    [InlineData("/annuaire/pieces/nouveau", "/stock")]
    [InlineData("/parametres", "/stock")]
    public void Ouvre_la_meme_piece_dans_l_autre_application(string depuis, string attendu) =>
        Assert.Equal(attendu, Bascule.Cible(depuis, Stock));

    [Fact]
    public void Un_resultat_s_ouvre_dans_l_application_courante()
    {
        Assert.Equal("/stock/pieces/5", Bascule.Lien(TypesObjet.PieceFinie, 5, "/stock/alertes"));
        Assert.Equal("/annuaire/pieces/5", Bascule.Lien(TypesObjet.PieceFinie, 5, "/annuaire"));
        Assert.Equal("/annuaire/clients/7", Bascule.Lien(TypesObjet.Client, 7, "/stock"));
    }

    [Fact]
    public void Formulaire_de_creation_avec_retour() =>
        Assert.Equal("/annuaire/bruts/nouveau?retour=%2Fstock%2Fbruts", Bascule.LienCreation(TypesObjet.Brut, "/stock/bruts"));
}
