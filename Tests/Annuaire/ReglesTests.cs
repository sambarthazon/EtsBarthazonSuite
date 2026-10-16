using Barthazon.Annuaire.Services;
using Barthazon.Commun.Contrats;

namespace Barthazon.Tests.Annuaire;

public class ReglesTests
{
    [Theory]
    [InlineData("A", "B")]
    [InlineData("Z", "AA")]
    [InlineData("AZ", "BA")]
    [InlineData("1", "2")]
    [InlineData("9", "10")]
    [InlineData("09", "10")]
    [InlineData("B2", "B3")]
    [InlineData("c", "d")]
    [InlineData("", "A")]
    public void Indice_suivant(string actuel, string attendu) => Assert.Equal(attendu, Indices.Suivant(actuel));

    [Fact]
    public void Libelle_automatique_d_un_rond() =>
        Assert.Equal("Rond Ø40 × 120 — 42CrMo4", LibelleBrut.Calculer(FormeBrut.RondPlein, 40, null, null, null, null, null, 120, null, "42CrMo4"));

    [Fact]
    public void Longueur_facultative_et_decimales_utiles() =>
        Assert.Equal("Plat 50 × 12,5 — S235", LibelleBrut.Calculer(FormeBrut.Plat, null, null, 50, 12.5m, null, null, null, null, "S235"));

    [Fact]
    public void Tube_avec_les_deux_diametres() =>
        Assert.Equal("Tube Ø40 / Ø30 × 1000 — 304L", LibelleBrut.Calculer(FormeBrut.Tube, 40, 30, null, null, null, null, 1000, null, "304L"));

    [Theory]
    [InlineData(FormeBrut.RondPlein, "Indiquez le diamètre.")]
    [InlineData(FormeBrut.Plat, "Indiquez la largeur et l'épaisseur.")]
    [InlineData(FormeBrut.Autre, "Décrivez le brut.")]
    public void Dimensions_demandees_selon_la_forme(FormeBrut forme, string erreur) =>
        Assert.Equal(erreur, LibelleBrut.Verifier(forme, null, null, null, null, null, null, null, null));

    [Fact]
    public void Tube_interieur_plus_petit() =>
        Assert.NotNull(LibelleBrut.Verifier(FormeBrut.Tube, 30, 40, null, null, null, null, null, null));

    [Fact]
    public void References_de_brut() => Assert.Equal(("BR-0007", 12), (LibelleBrut.Reference(7), LibelleBrut.Numero("BR-0012")));
}
