using Barthazon.Annuaire.Donnees;
using Barthazon.Annuaire.Services;
using Barthazon.Commun.Contrats;
using Barthazon.Commun.Donnees;
using Barthazon.Commun.Historique;
using Barthazon.Commun.Securite;

namespace Barthazon.Tests.Annuaire;

/// <summary>Règles de gestion de l'Annuaire (5.9), sur SQLite en mémoire.</summary>
public sealed class AnnuaireTests : IDisposable
{
    private readonly BaseMemoire _base = new();
    private readonly string _dossier = Path.Combine(Path.GetTempPath(), "bz-tests-" + Guid.NewGuid().ToString("N"));
    private readonly StockFactice _stock = new();
    private readonly ServiceReferentiels _referentiels;
    private readonly ServiceBruts _bruts;
    private readonly ServicePiecesFinies _pieces;
    private readonly ServiceHistorique _historique;
    private readonly FabriqueTest<AnnuaireDbContext> _annuaire;

    public AnnuaireTests()
    {
        var horloge = new HorlogeTest(new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero));
        var courant = Utilisateurs.Avec(1, Role.Gestion, "Marc");
        var commun = _base.Fabrique<CommunDbContext>(o => new(o));
        var annuaire = _annuaire = _base.Fabrique<AnnuaireDbContext>(o => new(o));
        _historique = new ServiceHistorique(commun, courant, horloge);
        var utilisateurs = new ServiceUtilisateurs(commun, courant, horloge);
        var options = new OptionsBarthazon { CheminDonnees = _dossier };
        _referentiels = new ServiceReferentiels(annuaire, courant, _historique);
        _bruts = new ServiceBruts(annuaire, courant, _historique, _stock);
        _pieces = new ServicePiecesFinies(annuaire, courant, _historique, utilisateurs, new StockagePlans(options), _stock, horloge);
    }

    public void Dispose()
    {
        _base.Dispose();
        if (Directory.Exists(_dossier))
        {
            Directory.Delete(_dossier, true);
        }
    }

    private async Task<int> Client(string nom = "Dupont SA") =>
        (await _referentiels.EnregistrerTiers(SorteTiers.Client, null, new SaisieTiers(nom, null, null, null, null))).Valeur;

    private async Task<int> Brut(decimal diametre = 40)
    {
        var matiere = (await _referentiels.CreerMatiere("42CrMo4")).Valeur;
        var resultat = await _bruts.Creer(new SaisieBrut { Forme = FormeBrut.RondPlein, Diametre = diametre, Longueur = 120, MatiereId = matiere });
        Assert.True(resultat.Reussi, resultat.Erreur);
        return resultat.Valeur;
    }

    private async Task<int> Piece(int client, params int[] bruts)
    {
        var resultat = await _pieces.Creer(new SaisieCreationPiece
        {
            Reference = "4512-B",
            Designation = "Axe de pompe",
            ClientId = client,
            Bruts = bruts.Select((b, i) => new BrutChoisi(b, i == 0, 1)).ToList(),
        });
        Assert.True(resultat.Reussi, resultat.Erreur);
        return resultat.Valeur;
    }

    [Fact]
    public async Task References_de_brut_automatiques()
    {
        await Brut(40);
        await Brut(45);
        var liste = await _bruts.Lister();
        Assert.Equal(["BR-0001", "BR-0002"], liste.Select(b => b.Reference));
        Assert.Equal("Rond Ø45 × 120 — 42CrMo4", liste[1].Libelle);
    }

    [Fact]
    public async Task Pas_de_piece_sans_brut()
    {
        var resultat = await _pieces.Creer(new SaisieCreationPiece { Reference = "X", Designation = "Y", ClientId = await Client() });
        Assert.False(resultat.Reussi);
    }

    [Fact]
    public async Task Reference_unique_pour_un_meme_client()
    {
        var client = await Client();
        var brut = await Brut();
        await Piece(client, brut);
        var doublon = await _pieces.Creer(new SaisieCreationPiece { Reference = "4512-B", Designation = "Autre", ClientId = client, Bruts = [new(brut, true, 1)] });
        Assert.False(doublon.Reussi);

        var autreClient = await _pieces.Creer(new SaisieCreationPiece { Reference = "4512-B", Designation = "Autre", ClientId = await Client("Martin"), Bruts = [new(brut, true, 1)] });
        Assert.True(autreClient.Reussi);
    }

    [Fact]
    public async Task Impossible_de_retirer_le_dernier_brut()
    {
        var (b1, b2) = (await Brut(40), await Brut(45));
        var piece = await Piece(await Client(), b1, b2);
        Assert.True((await _pieces.RetirerBrut(piece, b1)).Reussi);
        Assert.False((await _pieces.RetirerBrut(piece, b2)).Reussi);

        // Le brut restant est devenu principal.
        var fiche = await _pieces.Obtenir(piece);
        Assert.True(fiche!.Bruts.Single().Principal);
    }

    [Fact]
    public async Task Brut_utilise_ou_mouvemente_non_supprimable()
    {
        var utilise = await Brut(40);
        await Piece(await Client(), utilise);
        Assert.Contains("Archivez", (await _bruts.Supprimer(utilise)).Erreur, StringComparison.Ordinal);

        var mouvemente = await Brut(50);
        _stock.Mouvementes.Add((TypeReference.Brut, mouvemente));
        Assert.False((await _bruts.Supprimer(mouvemente)).Reussi);

        var libre = await Brut(60);
        Assert.True((await _bruts.Supprimer(libre)).Reussi);
    }

    [Fact]
    public async Task Piece_mouvementee_seulement_archivable()
    {
        var piece = await Piece(await Client(), await Brut());
        _stock.Mouvementes.Add((TypeReference.PieceFinie, piece));
        Assert.False((await _pieces.Supprimer(piece)).Reussi);
        Assert.True((await _pieces.Archiver(piece, true)).Reussi);
        Assert.Empty(await _pieces.Lister());
        Assert.Single(await _pieces.Lister(inclureArchivees: true));
    }

    [Fact]
    public async Task Nouvel_indice_recopie_gamme_et_specifications_l_ancien_reste_consultable()
    {
        var piece = await Piece(await Client(), await Brut());
        var a = (await _pieces.Obtenir(piece))!.IndiceCourant;
        await _pieces.EnregistrerOperation(a.Id, null, new SaisieOperation { Description = "Tournage" });
        await _pieces.EnregistrerOperation(a.Id, null, new SaisieOperation { Description = "Contrôle" });
        var specification = SaisieSpecification.ParDefaut();
        specification.Lignes[0].Valeur = "1,2";
        specification.Lignes.Add(new() { Libelle = "Traitement thermique", Valeur = "Trempe" });
        await _pieces.EnregistrerSpecification(a.Id, specification);

        Assert.False((await _pieces.NouvelIndice(piece, "B", "", true)).Reussi);
        Assert.True((await _pieces.NouvelIndice(piece, "B", "Cote modifiée", true)).Reussi);

        var fiche = (await _pieces.Obtenir(piece))!;
        Assert.Equal("B", fiche.IndiceCourant.Code);
        Assert.Equal([10, 20], fiche.IndiceCourant.Operations.Select(o => o.Numero));
        Assert.Equal(["Masse : 1,2 kg", "Traitement thermique : Trempe"], fiche.IndiceCourant.Specification.Lignes.Select(l => $"{l.Libelle} : {l.ValeurAffichee}"));

        // L'indice A reste consultable, en lecture seule.
        var ancien = (await _pieces.ObtenirIndice(a.Id))!;
        Assert.False(ancien.Courant);
        Assert.Equal(2, ancien.Operations.Count);
        Assert.False((await _pieces.EnregistrerOperation(a.Id, null, new SaisieOperation { Description = "Interdit" })).Reussi);

        var historique = await _historique.Lister(TypesObjet.PieceFinie, piece);
        Assert.Contains(historique, h => h.Phrase.StartsWith("Marc a passé l'indice de A à B", StringComparison.Ordinal));
        Assert.Contains(historique, h => h.Phrase == "Marc a créé la pièce 4512-B (Client Dupont SA)");
    }

    [Fact]
    public async Task Specifications_masse_par_defaut_et_lignes_libres()
    {
        var piece = await Piece(await Client(), await Brut());
        var indice = (await _pieces.Obtenir(piece))!.IndiceCourant;

        // Masse proposée par défaut ; laissée vide, elle n'est pas enregistrée.
        var saisie = SaisieSpecification.Depuis(indice.Specification);
        Assert.Equal("Masse", Assert.Single(saisie.Lignes).Libelle);
        saisie.Lignes.Add(new() { Libelle = "Longueur", Valeur = "120", Unite = "mm" });
        saisie.Lignes.Add(new() { Libelle = "Rugosité", Valeur = "Ra 0,8" });
        Assert.True((await _pieces.EnregistrerSpecification(indice.Id, saisie)).Reussi);
        var lignes = (await _pieces.Obtenir(piece))!.IndiceCourant.Specification.Lignes;
        Assert.Equal(["Longueur : 120 mm", "Rugosité : Ra 0,8"], lignes.Select(l => $"{l.Libelle} : {l.ValeurAffichee}"));

        // Une valeur sans dire ce que c'est est refusée.
        var sansLibelle = new SaisieSpecification { Lignes = [new() { Valeur = "40" }] };
        Assert.False((await _pieces.EnregistrerSpecification(indice.Id, sansLibelle)).Reussi);
    }

    [Fact]
    public async Task Gamme_reordonnee_numeros_croissants()
    {
        var piece = await Piece(await Client(), await Brut());
        var indice = (await _pieces.Obtenir(piece))!.IndiceCourant.Id;
        foreach (var d in new[] { "A", "B", "C" })
        {
            await _pieces.EnregistrerOperation(indice, null, new SaisieOperation { Description = d });
        }

        var c = (await _pieces.ObtenirIndice(indice))!.Operations[2];
        await _pieces.DeplacerOperation(indice, c.Id, -1);
        var operations = (await _pieces.ObtenirIndice(indice))!.Operations;
        Assert.Equal(["A", "C", "B"], operations.Select(o => o.Description));
        Assert.Equal([10, 20, 30], operations.Select(o => o.Numero));
    }

    [Fact]
    public async Task Lien_remplace_reciproque()
    {
        var client = await Client();
        var brut = await Brut();
        var ancienne = await Piece(client, brut);
        var nouvelle = (await _pieces.Creer(new SaisieCreationPiece { Reference = "4512-C", Designation = "Axe", ClientId = client, Bruts = [new(brut, true, 1)] })).Valeur;

        Assert.True((await _pieces.AjouterLien(nouvelle, TypeLien.Remplace, ancienne, null, null)).Reussi);
        var lienAncienne = Assert.Single((await _pieces.Obtenir(ancienne))!.Liens);
        Assert.Equal(TypeLien.RemplaceePar, lienAncienne.Type);
        Assert.Equal(nouvelle, lienAncienne.PieceCibleId);

        var lienNouvelle = Assert.Single((await _pieces.Obtenir(nouvelle))!.Liens);
        Assert.True((await _pieces.SupprimerLien(nouvelle, lienNouvelle.Id)).Reussi);
        Assert.Empty((await _pieces.Obtenir(ancienne))!.Liens);
    }

    [Fact]
    public async Task Plans_rattaches_a_l_indice()
    {
        var piece = await Piece(await Client(), await Brut());
        var indice = (await _pieces.Obtenir(piece))!.IndiceCourant.Id;
        using var flux = new MemoryStream([1, 2, 3]);
        Assert.False((await _pieces.AjouterPlan(indice, "plan.exe", flux, 3)).Reussi);
        Assert.True((await _pieces.AjouterPlan(indice, "4512-B.pdf", flux, 3)).Reussi);
        var plan = Assert.Single((await _pieces.Obtenir(piece))!.Plans);
        Assert.True(File.Exists(Path.Combine(_dossier, "plans", piece.ToString(System.Globalization.CultureInfo.InvariantCulture), "A", "4512-B.pdf")));
        Assert.NotNull(await _pieces.FichierPlan(plan.Id));
    }

    [Fact]
    public async Task Consultation_ne_modifie_rien()
    {
        var service = new ServiceBruts(_annuaire, Utilisateurs.Avec(2, Role.Atelier), _historique, _stock);
        var resultat = await service.Creer(new SaisieBrut { Forme = FormeBrut.RondPlein, Diametre = 10, MatiereId = 1 });
        Assert.False(resultat.Reussi);
    }

    private sealed class StockFactice : IStockLecture
    {
        public HashSet<(TypeReference, int)> Mouvementes { get; } = [];

        public Task<ApercuStock?> ApercuPieceFinie(int pieceFinieId) => Task.FromResult<ApercuStock?>(null);

        public Task<ApercuStock?> ApercuBrut(int brutId) => Task.FromResult<ApercuStock?>(null);

        public Task<bool> ADesMouvements(TypeReference type, int id) => Task.FromResult(Mouvementes.Contains((type, id)));

        public Task<int> NombreAlertes() => Task.FromResult(0);
    }
}
