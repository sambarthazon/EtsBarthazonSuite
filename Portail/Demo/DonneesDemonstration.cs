using System.Security.Claims;
using Barthazon.Commun.Contrats;
using Barthazon.Commun.Securite;
#if !SANS_ANNUAIRE
using Barthazon.Annuaire.Services;
#endif
#if !SANS_STOCK
using Barthazon.Stock.Donnees;
using Barthazon.Stock.Services;
#endif

namespace Barthazon.Portail.Demo;

/// <summary>
/// Jeu de données de démonstration, créé uniquement en développement (Barthazon:DonneesDemonstration)
/// et seulement si la base ne contient encore aucun utilisateur.
/// Utilisateurs : Sam (Administrateur, 0000), Marc (Gestion, 1111), Julie (Atelier, 2222), Paul (Consultation, 3333).
/// </summary>
internal sealed class DonneesDemonstration(IServiceProvider services, ILogger<DonneesDemonstration> journal)
{
    public async Task Creer()
    {
        var utilisateurs = services.GetRequiredService<ServiceUtilisateurs>();
        if (await utilisateurs.ExisteUtilisateur())
        {
            return;
        }

        journal.LogInformation("Création des données de démonstration");
        var admin = (await utilisateurs.CreerPremierAdministrateur("Sam", "Barthazon", "0000")).Valeur;
        services.GetRequiredService<UtilisateurCourant>().Definir(Principal(admin, "Sam", Role.Administrateur));
        await utilisateurs.Creer(new SaisieUtilisateur("Marc", "Durand", Role.Gestion, true, "1111"));
        await utilisateurs.Creer(new SaisieUtilisateur("Julie", "Petit", Role.Atelier, true, "2222"));
        await utilisateurs.Creer(new SaisieUtilisateur("Paul", "Morel", Role.Consultation, true, "3333"));

#if !SANS_ANNUAIRE
        var pieces = await Annuaire();
#if !SANS_STOCK
        await Stock(pieces);
#endif
#endif
    }

    private static ClaimsPrincipal Principal(int id, string prenom, Role role) =>
        Authentification.CreerPrincipal(new UtilisateurConnecte(id, prenom, prenom[..2], role, null), false, DateTimeOffset.MaxValue);

#if !SANS_ANNUAIRE
    private async Task<Dictionary<string, (int Piece, int[] Bruts)>> Annuaire()
    {
        var referentiels = services.GetRequiredService<ServiceReferentiels>();
        var bruts = services.GetRequiredService<ServiceBruts>();
        var pieces = services.GetRequiredService<ServicePiecesFinies>();

        async Task<int> Tiers(SorteTiers sorte, string nom, params (string Nom, string? Telephone, string? Email)[] contacts) =>
            (await referentiels.EnregistrerTiers(sorte, null, new SaisieTiers(nom, null,
                contacts.Select(c => new SaisieContact { Nom = c.Nom, Telephone = c.Telephone, Email = c.Email }).ToList()))).Valeur;

        var dupont = await Tiers(SorteTiers.Client, "Dupont SA", ("M. Dupont", "02 41 00 00 01", null), ("Service achats", "02 41 00 00 09", "achats@dupont.example"));
        var martin = await Tiers(SorteTiers.Client, "Martin Hydraulique", ("Mme Martin", "02 41 00 00 02", null));
        var lebrun = await Tiers(SorteTiers.Client, "Lebrun Agricole");
        var acier = await Tiers(SorteTiers.Fournisseur, "Acier Centre", ("Service commercial", "02 47 00 00 10", null));
        var metaux = await Tiers(SorteTiers.Fournisseur, "Métaux Ouest");
        var inox = await Tiers(SorteTiers.Fournisseur, "Inox Service");

        var matieres = (await referentiels.ListerMatieres()).ToDictionary(m => m.Libelle, m => m.Id);

        async Task<int> Brut(FormeBrut forme, string matiere, Unite unite, Action<SaisieBrut> dimensions, params (int Fournisseur, string? Reference)[] fournisseurs)
        {
            var saisie = new SaisieBrut
            {
                Forme = forme,
                MatiereId = matieres[matiere],
                Unite = unite,
                Fournisseurs = fournisseurs.Select((f, i) => new SaisieFournisseurBrut(f.Fournisseur, f.Reference, i == 0)).ToList(),
            };
            dimensions(saisie);
            return (await bruts.Creer(saisie)).Valeur;
        }

        var rond40 = await Brut(FormeBrut.RondPlein, "42CrMo4", Unite.Piece, s => (s.Diametre, s.Longueur) = (40, 120), (acier, "R40-42CD4-120"), (metaux, "M-4042"));
        var rond45 = await Brut(FormeBrut.RondPlein, "42CrMo4", Unite.Piece, s => (s.Diametre, s.Longueur) = (45, 120), (acier, "R45-42CD4-120"));
        var barre30 = await Brut(FormeBrut.RondPlein, "C45", Unite.Barre, s => (s.Diametre, s.Longueur) = (30, 1000), (metaux, null));
        var tube = await Brut(FormeBrut.Tube, "304L", Unite.Metre, s => (s.Diametre, s.DiametreInterieur) = (50, 40), (inox, "T50x5-304L"));
        var plat = await Brut(FormeBrut.Plat, "S235", Unite.Barre, s => (s.Largeur, s.Epaisseur, s.Longueur) = (60, 10, 2000), (acier, null));
        var lopin = await Brut(FormeBrut.RondPlein, "AU4G (2017)", Unite.Lopin, s => (s.Diametre, s.Longueur) = (80, 35), (metaux, null));

        var postes = (await referentiels.ListerPostes()).ToDictionary(p => p.Libelle, p => p.Id);
        var resultat = new Dictionary<string, (int, int[])>();

        async Task Piece(string reference, string designation, int client, (int Brut, int ParBrut)[] brutsPiece, string papier,
            string? traitement, string tolerance, params (string Poste, string Description, int Temps)[] gamme)
        {
            var id = (await pieces.Creer(new SaisieCreationPiece
            {
                Reference = reference,
                Designation = designation,
                ClientId = client,
                EmplacementPlanPapier = papier,
                Bruts = brutsPiece.Select((b, i) => new BrutChoisi(b.Brut, i == 0, b.ParBrut)).ToList(),
                Specification = new SaisieSpecification
                {
                    Lignes =
                    [
                        new() { Libelle = "Tolérance générale", Valeur = tolerance },
                        .. traitement is null ? [] : new[] { new SaisieSpecification.LigneSaisie { Libelle = "Traitement thermique", Valeur = traitement } },
                    ],
                },
            })).Valeur;
            var indice = (await pieces.Obtenir(id))!.IndiceCourant.Id;
            foreach (var (poste, description, temps) in gamme)
            {
                await pieces.EnregistrerOperation(indice, null, new SaisieOperation { PosteId = postes[poste], Description = description, TempsEstimeMin = temps });
            }

            resultat[reference] = (id, brutsPiece.Select(b => b.Brut).ToArray());
        }

        await Piece("4512-B", "Axe de pompe", dupont, [(rond40, 1), (rond45, 1)], "Classeur Dupont, chemise 12", "Trempe + revenu", "ISO 2768-m",
            ("Tour CN", "Dressage, chariotage Ø36 h7, gorge", 12), ("Fraiseuse", "Méplat 10 mm", 6), ("Traitement extérieur", "Trempe + revenu", 0), ("Contrôle", "Contrôle dimensionnel", 4));
        await Piece("4513-A", "Entretoise", dupont, [(barre30, 8)], "Classeur Dupont, chemise 13", null, "ISO 2768-m",
            ("Tour", "Tronçonnage et chanfreins", 3), ("Contrôle", "Contrôle", 1));
        await Piece("HY-220", "Corps de distributeur", martin, [(lopin, 1)], "Classeur Martin", null, "ISO 2768-f",
            ("Centre d'usinage", "Usinage face 1 et alésages", 35), ("Centre d'usinage", "Usinage face 2", 25), ("Contrôle", "Contrôle tridimensionnel", 10));
        await Piece("HY-305", "Fourreau inox", martin, [(tube, 6)], "Classeur Martin", null, "ISO 2768-m",
            ("Tour CN", "Alésage Ø42 H8, chanfreins", 9));
        await Piece("LB-7781", "Patte de fixation", lebrun, [(plat, 20)], "Armoire bureau, tiroir 2", null, "ISO 2768-c",
            ("Fraiseuse", "Découpe et perçages", 5), ("Soudure", "Soudure du renfort", 8));
        return resultat;
    }
#endif

#if !SANS_ANNUAIRE && !SANS_STOCK
    private async Task Stock(Dictionary<string, (int Piece, int[] Bruts)> pieces)
    {
        var mouvements = services.GetRequiredService<ServiceMouvements>();
        var axe = pieces["4512-B"];
        var entretoise = pieces["4513-A"];
        var corps = pieces["HY-220"];
        var patte = pieces["LB-7781"];
        var hier = DateTime.Today.AddDays(-1);

        await mouvements.EnregistrerArrivage(new DemandeArrivage(axe.Bruts[0], 50, null, hier, "8812", null));
        await mouvements.EnregistrerFabrication(new DemandeFabrication(axe.Piece, axe.Bruts[0], 10, 10, false, null, null));
        await mouvements.EnregistrerSortie(new DemandeSortie(TypeReference.PieceFinie, axe.Piece, 4, MotifSortie.LivraisonClient, null, null, "C-1021", null));
        await mouvements.ModifierSeuil(TypeReference.PieceFinie, axe.Piece, 5);

        await mouvements.EnregistrerArrivage(new DemandeArrivage(entretoise.Bruts[0], 6, null, hier, null, null));
        await mouvements.EnregistrerFabrication(new DemandeFabrication(entretoise.Piece, entretoise.Bruts[0], 2, 16, false, null, "Série courte"));
        await mouvements.ModifierSeuil(TypeReference.Brut, entretoise.Bruts[0], 5);

        await mouvements.EnregistrerAjustement(new DemandeAjustement(TypeReference.PieceFinie, corps.Piece, 3, "Stock de départ"));
        await mouvements.EnregistrerAjustement(new DemandeAjustement(TypeReference.Brut, corps.Bruts[0], 1, "Stock de départ"));
        await mouvements.EnregistrerSortie(new DemandeSortie(TypeReference.Brut, corps.Bruts[0], 1, MotifSortie.Rebut, null, null, null, "Lopin fissuré"));

        await mouvements.EnregistrerArrivage(new DemandeArrivage(patte.Bruts[0], 4, null, null, null, null));
        await mouvements.ModifierSeuil(TypeReference.Brut, patte.Bruts[0], 2);
    }
#endif
}
