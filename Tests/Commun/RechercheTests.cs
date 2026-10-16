using Barthazon.Commun.Outils;

namespace Barthazon.Tests.Commun;

public class RechercheTests
{
    [Theory]
    [InlineData("40")]
    [InlineData("42cr")]
    [InlineData("rond 42CR")]
    [InlineData("ø40")]
    public void Retrouve_un_brut_par_son_diametre_ou_sa_matiere(string requete) =>
        Assert.True(Recherche.Correspond(requete, "Rond Ø40 × 120 — 42CrMo4"));

    [Fact]
    public void Ignore_accents_et_casse() => Assert.True(Recherche.Correspond("tole", "Tôle ép. 3"));

    [Fact]
    public void Tous_les_mots_doivent_correspondre() => Assert.False(Recherche.Correspond("40 inox", "Rond Ø40 × 120 — 42CrMo4"));

    [Fact]
    public void Requete_vide_correspond_a_tout() => Assert.True(Recherche.Correspond("  ", "x"));
}
