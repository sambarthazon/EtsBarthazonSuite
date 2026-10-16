using Barthazon.Commun.Donnees;
using Barthazon.Commun.Securite;

namespace Barthazon.Tests.Commun;

public sealed class UtilisateursTests : IDisposable
{
    private readonly BaseMemoire _base = new();
    private readonly HorlogeTest _horloge = new(new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero));
    private readonly ServiceUtilisateurs _service;

    public UtilisateursTests() =>
        _service = new ServiceUtilisateurs(_base.Fabrique<CommunDbContext>(o => new(o)), Utilisateurs.Avec(1, Role.Administrateur), _horloge);

    public void Dispose() => _base.Dispose();

    [Fact]
    public async Task Premier_administrateur_une_seule_fois()
    {
        Assert.True((await _service.CreerPremierAdministrateur("Sam", "B", "1234")).Reussi);
        Assert.False((await _service.CreerPremierAdministrateur("Autre", "", "9999")).Reussi);
    }

    [Theory]
    [InlineData("123")]
    [InlineData("1234567")]
    [InlineData("12a4")]
    public async Task Le_pin_fait_4_a_6_chiffres(string pin) =>
        Assert.False((await _service.CreerPremierAdministrateur("Sam", "", pin)).Reussi);

    [Fact]
    public async Task Bloque_une_minute_apres_cinq_erreurs()
    {
        var id = (await _service.CreerPremierAdministrateur("Sam", "B", "1234")).Valeur;
        for (var i = 0; i < 5; i++)
        {
            Assert.False((await _service.VerifierPin(id, "0000")).Reussi);
        }

        var bloque = await _service.VerifierPin(id, "1234");
        Assert.False(bloque.Reussi);
        Assert.Contains("réessayez", bloque.Erreur, StringComparison.Ordinal);

        _horloge.Maintenant += TimeSpan.FromSeconds(61);
        var connecte = await _service.VerifierPin(id, "1234");
        Assert.True(connecte.Reussi);
        Assert.Equal(Role.Administrateur, connecte.Valeur!.Role);
    }

    [Fact]
    public async Task Garde_au_moins_un_administrateur_actif()
    {
        var id = (await _service.CreerPremierAdministrateur("Sam", "B", "1234")).Valeur;
        var resultat = await _service.Modifier(id, new SaisieUtilisateur("Sam", "B", Role.Atelier, true, null));
        Assert.False(resultat.Reussi);
    }

    [Fact]
    public void Initiales() => Assert.Equal("MB", ServiceUtilisateurs.CalculerInitiales("marc", "barthazon"));
}
