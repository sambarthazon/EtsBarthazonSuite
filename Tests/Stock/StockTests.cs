using Barthazon.Commun.Composants;
using Barthazon.Commun.Contrats;
using Barthazon.Commun.Donnees;
using Barthazon.Commun.Securite;
using Barthazon.Stock.Donnees;
using Barthazon.Stock.Services;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Tests.Stock;

/// <summary>Calcul du stock : mouvements, niveaux, annulations, alertes (SQLite en mémoire, Annuaire factice).</summary>
public sealed class StockTests : IDisposable
{
    private const int PieceId = 1;
    private const int BrutPrincipal = 10;
    private const int BrutSecondaire = 11;

    private readonly BaseMemoire _base = new();
    private readonly HorlogeTest _horloge = new(new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero));
    private readonly FabriqueTest<StockDbContext> _fabrique;
    private readonly FabriqueTest<CommunDbContext> _commun;
    private readonly AnnuaireFactice _annuaire = new();

    public StockTests()
    {
        _commun = _base.Fabrique<CommunDbContext>(o => new(o));
        _fabrique = _base.Fabrique<StockDbContext>(o => new(o));
    }

    public void Dispose() => _base.Dispose();

    private ServiceMouvements Service(int utilisateur = 1, Role role = Role.Gestion) =>
        new(_fabrique, _annuaire, Utilisateurs.Avec(utilisateur, role), new SignauxApplication(), _horloge);

    private ServiceStock Consultation(ServiceMouvements mouvements) =>
        new(_fabrique, _annuaire, new ServiceUtilisateurs(_commun, Utilisateurs.Avec(1, Role.Gestion), _horloge), mouvements);

    private async Task<decimal> Quantite(TypeReference type, int id)
    {
        await using var db = await _fabrique.CreateDbContextAsync();
        return (await db.Niveaux.FirstOrDefaultAsync(n => n.TypeReference == type && n.ReferenceId == id))?.Quantite ?? 0;
    }

    [Fact]
    public async Task Arrivage_ajoute_au_stock_du_brut()
    {
        var resultat = await Service().EnregistrerArrivage(new DemandeArrivage(BrutSecondaire, 50, 7, null, "8812", null));
        Assert.True(resultat.Reussi, resultat.Erreur);
        Assert.Equal("Arrivage enregistré : +50 barres de BR-0011", resultat.Valeur!.Message);
        Assert.Equal(50, await Quantite(TypeReference.Brut, BrutSecondaire));

        await using var db = await _fabrique.CreateDbContextAsync();
        var mouvement = await db.Mouvements.SingleAsync();
        Assert.Equal((TypeMouvement.Arrivage, 50m, 50m, 7, "8812"), (mouvement.TypeMouvement, mouvement.Quantite, mouvement.QuantiteApres, mouvement.FournisseurId!.Value, mouvement.NumeroBL!));
    }

    [Fact]
    public async Task Fabrication_cree_deux_mouvements_lies()
    {
        var service = Service();
        await service.EnregistrerArrivage(new DemandeArrivage(BrutPrincipal, 40, null, null, null, null));
        var resultat = await service.EnregistrerFabrication(new DemandeFabrication(PieceId, BrutPrincipal, 10, 10, false, null, null));
        Assert.True(resultat.Reussi, resultat.Erreur);
        Assert.Equal("Fabrication enregistrée : +10 pièces 4512-B (−10 barres BR-0010)", resultat.Valeur!.Message);

        Assert.Equal(30, await Quantite(TypeReference.Brut, BrutPrincipal));
        Assert.Equal(10, await Quantite(TypeReference.PieceFinie, PieceId));

        await using var db = await _fabrique.CreateDbContextAsync();
        var fabrication = await db.Mouvements.Where(m => m.TypeMouvement == TypeMouvement.Fabrication).ToListAsync();
        Assert.Equal(2, fabrication.Count);
        Assert.Single(fabrication.Select(m => m.OperationId).Distinct());
    }

    [Fact]
    public async Task Fabrication_sans_deduire_de_brut()
    {
        var resultat = await Service().EnregistrerFabrication(new DemandeFabrication(PieceId, null, 0, 6, true, null, null));
        Assert.True(resultat.Reussi, resultat.Erreur);
        Assert.Equal(6, await Quantite(TypeReference.PieceFinie, PieceId));
        Assert.Equal(0, await Quantite(TypeReference.Brut, BrutPrincipal));
    }

    [Fact]
    public async Task Fabrication_avec_un_brut_non_associe_refusee()
    {
        var resultat = await Service().EnregistrerFabrication(new DemandeFabrication(PieceId, 99, 1, 1, false, null, null));
        Assert.False(resultat.Reussi);
    }

    [Fact]
    public async Task Sortie_retire_du_stock()
    {
        var service = Service();
        await service.EnregistrerFabrication(new DemandeFabrication(PieceId, null, 0, 10, true, null, null));
        var resultat = await service.EnregistrerSortie(new DemandeSortie(TypeReference.PieceFinie, PieceId, 4, MotifSortie.LivraisonClient, 3, null, "BL 12", null));
        Assert.True(resultat.Reussi, resultat.Erreur);
        Assert.Equal(6, await Quantite(TypeReference.PieceFinie, PieceId));

        // Motif réservé aux bruts.
        Assert.False((await service.EnregistrerSortie(new DemandeSortie(TypeReference.PieceFinie, PieceId, 1, MotifSortie.RetourFournisseur, null, null, null, null))).Reussi);
    }

    [Fact]
    public async Task Stock_negatif_autorise()
    {
        var resultat = await Service().EnregistrerSortie(new DemandeSortie(TypeReference.Brut, BrutPrincipal, 3, MotifSortie.Rebut, null, null, null, null));
        Assert.True(resultat.Reussi, resultat.Erreur);
        Assert.Equal(-3, await Quantite(TypeReference.Brut, BrutPrincipal));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-2)]
    public async Task Quantite_strictement_positive(decimal quantite) =>
        Assert.False((await Service().EnregistrerArrivage(new DemandeArrivage(BrutPrincipal, quantite, null, null, null, null))).Reussi);

    [Fact]
    public async Task Pas_de_date_dans_le_futur()
    {
        var demain = _horloge.GetLocalNow().Date.AddDays(1);
        Assert.False((await Service().EnregistrerArrivage(new DemandeArrivage(BrutPrincipal, 1, null, demain, null, null))).Reussi);

        var hier = _horloge.GetLocalNow().Date.AddDays(-1);
        Assert.True((await Service().EnregistrerArrivage(new DemandeArrivage(BrutPrincipal, 1, null, hier, null, null))).Reussi);
    }

    [Fact]
    public async Task Ajustement_egal_a_l_ecart()
    {
        var service = Service();
        await service.EnregistrerFabrication(new DemandeFabrication(PieceId, null, 0, 12, true, null, null));
        var resultat = await service.EnregistrerAjustement(new DemandeAjustement(TypeReference.PieceFinie, PieceId, 10, "inventaire"));
        Assert.True(resultat.Reussi, resultat.Erreur);
        Assert.Contains("écart −2 pièces", resultat.Valeur!.Message, StringComparison.Ordinal);
        Assert.Equal(10, await Quantite(TypeReference.PieceFinie, PieceId));

        // Pas d'écart : rien à enregistrer.
        Assert.False((await service.EnregistrerAjustement(new DemandeAjustement(TypeReference.PieceFinie, PieceId, 10, null))).Reussi);
    }

    [Fact]
    public async Task Ajustement_reserve_a_la_gestion() =>
        Assert.False((await Service(role: Role.Atelier).EnregistrerAjustement(new DemandeAjustement(TypeReference.Brut, BrutPrincipal, 5, null))).Reussi);

    [Fact]
    public async Task Consultation_ne_saisit_pas_de_mouvement() =>
        Assert.False((await Service(role: Role.Consultation).EnregistrerArrivage(new DemandeArrivage(BrutPrincipal, 5, null, null, null, null))).Reussi);

    [Fact]
    public async Task Annuler_une_fabrication_remet_les_deux_stocks()
    {
        var service = Service();
        await service.EnregistrerArrivage(new DemandeArrivage(BrutPrincipal, 40, null, null, null, null));
        var fabrication = await service.EnregistrerFabrication(new DemandeFabrication(PieceId, BrutPrincipal, 10, 10, false, null, null));

        // Annuler à partir du mouvement de la pièce : la fabrication s'annule en entier.
        var annulation = await service.Annuler(fabrication.Valeur!.Mouvements[1]);
        Assert.True(annulation.Reussi, annulation.Erreur);
        Assert.Equal(40, await Quantite(TypeReference.Brut, BrutPrincipal));
        Assert.Equal(0, await Quantite(TypeReference.PieceFinie, PieceId));

        await using var db = await _fabrique.CreateDbContextAsync();
        var originaux = await db.Mouvements.Where(m => m.TypeMouvement == TypeMouvement.Fabrication).ToListAsync();
        Assert.All(originaux, m => Assert.NotNull(m.AnnuleParId));
        Assert.Equal(2, await db.Mouvements.CountAsync(m => m.TypeMouvement == TypeMouvement.Annulation));

        // Ni double annulation, ni annulation d'une annulation.
        Assert.False((await service.Annuler(originaux[0].Id)).Reussi);
        var inverse = await db.Mouvements.FirstAsync(m => m.TypeMouvement == TypeMouvement.Annulation);
        Assert.False((await service.Annuler(inverse.Id)).Reussi);
    }

    [Fact]
    public async Task Annulation_par_l_auteur_le_jour_meme_seulement()
    {
        var auteur = Service(utilisateur: 5, role: Role.Atelier);
        var mouvement = (await auteur.EnregistrerArrivage(new DemandeArrivage(BrutPrincipal, 5, null, null, null, null))).Valeur!.Mouvements[0];
        var autre = (await auteur.EnregistrerArrivage(new DemandeArrivage(BrutPrincipal, 5, null, null, null, null))).Valeur!.Mouvements[0];

        Assert.False((await Service(utilisateur: 6, role: Role.Atelier).Annuler(mouvement)).Reussi);
        Assert.True((await auteur.Annuler(mouvement)).Reussi);

        _horloge.Maintenant += TimeSpan.FromDays(1);
        Assert.False((await auteur.Annuler(autre)).Reussi);
        Assert.True((await Service(role: Role.Gestion).Annuler(autre)).Reussi);
        Assert.Equal(0, await Quantite(TypeReference.Brut, BrutPrincipal));
    }

    [Theory]
    [InlineData(5, null, true, EtatStock.Ok)]
    [InlineData(0, null, false, EtatStock.Neutre)]
    [InlineData(0, 5, false, EtatStock.Neutre)]
    [InlineData(0, null, true, EtatStock.Rupture)]
    [InlineData(-3, 5, true, EtatStock.Rupture)]
    [InlineData(5, 5, true, EtatStock.SousLeSeuil)]
    [InlineData(6, 5, true, EtatStock.Ok)]
    public void Etats_d_alerte(decimal quantite, int? seuil, bool mouvemente, EtatStock attendu) =>
        Assert.Equal(attendu, ReglesStock.Etat(quantite, seuil, mouvemente));

    [Fact]
    public async Task Alerte_sous_le_seuil_avec_plus_de_brut()
    {
        var service = Service();
        await service.EnregistrerFabrication(new DemandeFabrication(PieceId, null, 0, 10, true, null, null));
        await service.ModifierSeuil(TypeReference.PieceFinie, PieceId, 5);
        var consultation = Consultation(service);
        Assert.Empty(await consultation.Alertes());

        await service.EnregistrerSortie(new DemandeSortie(TypeReference.PieceFinie, PieceId, 6, MotifSortie.LivraisonClient, null, null, null, null));
        var alerte = Assert.Single(await consultation.Alertes());
        Assert.Equal((EtatStock.SousLeSeuil, 4m, true), (alerte.Etat, alerte.Quantite, alerte.PlusDeBrut));

        // Rupture d'un brut : avant les pièces sous le seuil.
        await service.EnregistrerSortie(new DemandeSortie(TypeReference.Brut, BrutSecondaire, 1, MotifSortie.Rebut, null, null, null, null));
        var alertes = await consultation.Alertes();
        Assert.Equal([EtatStock.Rupture, EtatStock.SousLeSeuil], alertes.Select(a => a.Etat));
    }

    [Fact]
    public async Task References_de_l_annuaire_a_zero_et_brut_disponible()
    {
        var service = Service();
        var consultation = Consultation(service);
        var piece = Assert.Single(await consultation.ListerPieces());
        Assert.Equal((0m, EtatStock.Neutre), (piece.Quantite, piece.Etat));

        await service.EnregistrerArrivage(new DemandeArrivage(BrutPrincipal, 40, null, null, null, null));
        await service.EnregistrerArrivage(new DemandeArrivage(BrutSecondaire, 2, null, null, null, null));
        piece = Assert.Single(await consultation.ListerPieces());
        Assert.Equal(42, piece.BrutDisponible);
        Assert.Equal(Unite.Barre, piece.UniteBrut);
    }

    [Fact]
    public async Task Verifier_les_stocks_signale_les_ecarts()
    {
        var service = Service(role: Role.Administrateur);
        await service.EnregistrerArrivage(new DemandeArrivage(BrutPrincipal, 40, null, null, null, null));
        Assert.Empty(await service.Verifier());

        await using (var db = await _fabrique.CreateDbContextAsync())
        {
            await db.Niveaux.ExecuteUpdateAsync(s => s.SetProperty(n => n.Quantite, 39m));
        }

        var ecart = Assert.Single(await service.Verifier());
        Assert.Equal((39m, 40m), (ecart.Enregistre, ecart.Calcule));
        Assert.Equal(1, (await service.Corriger()).Valeur);
        Assert.Equal(40, await Quantite(TypeReference.Brut, BrutPrincipal));
    }

    [Fact]
    public async Task Quantites_decimales()
    {
        _annuaire.UniteSecondaire = Unite.Metre;
        var service = Service();
        await service.EnregistrerArrivage(new DemandeArrivage(BrutSecondaire, 2.5m, null, null, null, null));
        var resultat = await service.EnregistrerSortie(new DemandeSortie(TypeReference.Brut, BrutSecondaire, 0.75m, MotifSortie.HorsFabrication, null, null, null, null));
        Assert.Equal(1.75m, await Quantite(TypeReference.Brut, BrutSecondaire));
        Assert.Contains("−0,75 m", resultat.Valeur!.Message, StringComparison.Ordinal);
    }

    /// <summary>Annuaire réduit : une pièce finie 4512-B réalisable dans deux bruts (BR-0010 principal, BR-0011).</summary>
    private sealed class AnnuaireFactice : IAnnuaireLecture
    {
        public Unite UniteSecondaire { get; set; } = Unite.Barre;

        private static readonly PieceFinieResume Piece = new(PieceId, "4512-B", "Axe de pompe", 3, "Dupont SA", "C", false);

        private BrutResume Brut(int id) => new(id, $"BR-00{id}", id == BrutPrincipal ? "Rond Ø40 × 120 — 42CrMo4" : "Rond Ø45 × 120 — 42CrMo4",
            FormeBrut.RondPlein, 1, "42CrMo4", id == BrutPrincipal ? Unite.Barre : UniteSecondaire, 7, "Acier Centre", false);

        public Task<IReadOnlyList<PieceFinieResume>> ListerPiecesFinies(bool inclureArchivees = false) => Task.FromResult<IReadOnlyList<PieceFinieResume>>([Piece]);

        public Task<PieceFinieResume?> ObtenirPieceFinie(int id) => Task.FromResult(id == PieceId ? Piece : null);

        public Task<IReadOnlyList<BrutResume>> ListerBruts(bool inclureArchives = false) => Task.FromResult<IReadOnlyList<BrutResume>>([Brut(BrutPrincipal), Brut(BrutSecondaire)]);

        public Task<BrutResume?> ObtenirBrut(int id) => Task.FromResult(id is BrutPrincipal or BrutSecondaire ? Brut(id) : null);

        public Task<IReadOnlyList<BrutAssocie>> BrutsDeLaPiece(int pieceFinieId) =>
            Task.FromResult<IReadOnlyList<BrutAssocie>>(pieceFinieId == PieceId ? [new(PieceId, Brut(BrutPrincipal), true, 1), new(PieceId, Brut(BrutSecondaire), false, 1)] : []);

        public Task<IReadOnlyList<FournisseurBrut>> FournisseursDuBrut(int brutId) => Task.FromResult<IReadOnlyList<FournisseurBrut>>([new(7, "Acier Centre", null, true)]);

        public Task<IReadOnlyList<BrutAssocie>> ListerAssociations() => BrutsDeLaPiece(PieceId);

        public Task<IReadOnlyList<TiersResume>> ListerClients() => Task.FromResult<IReadOnlyList<TiersResume>>([new(3, "Dupont SA")]);

        public Task<IReadOnlyList<TiersResume>> ListerFournisseurs() => Task.FromResult<IReadOnlyList<TiersResume>>([new(7, "Acier Centre")]);
    }
}
