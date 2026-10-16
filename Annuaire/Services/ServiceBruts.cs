using Barthazon.Annuaire.Donnees;
using Barthazon.Commun.Contrats;
using Barthazon.Commun.Historique;
using Barthazon.Commun.Outils;
using Barthazon.Commun.Securite;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Annuaire.Services;

public sealed record LigneBrut(
    int Id,
    string Reference,
    string Libelle,
    FormeBrut Forme,
    int MatiereId,
    string Matiere,
    Unite Unite,
    int? FournisseurPrincipalId,
    string? FournisseurPrincipal,
    IReadOnlyList<int> FournisseursIds,
    int NombrePieces,
    bool Archive,
    string TexteRecherche);

public sealed record FournisseurDuBrut(int FournisseurId, string Nom, string? ReferenceFournisseur, bool Principal);

public sealed record PieceDuBrut(int Id, string Reference, string Designation, string Client, bool Principal, int PiecesParBrut, bool Archive);

public sealed record FicheBrut(
    int Id,
    string Reference,
    string Libelle,
    string LibelleAutomatique,
    FormeBrut Forme,
    decimal? Diametre,
    decimal? DiametreInterieur,
    decimal? Largeur,
    decimal? Epaisseur,
    decimal? Cote,
    decimal? SurPlats,
    decimal? Longueur,
    string? DescriptionForme,
    int MatiereId,
    string Matiere,
    Unite Unite,
    string? Designation,
    string? Remarques,
    bool Archive,
    IReadOnlyList<FournisseurDuBrut> Fournisseurs,
    IReadOnlyList<PieceDuBrut> Pieces);

public sealed record SaisieFournisseurBrut(int FournisseurId, string? ReferenceFournisseur, bool Principal);

public sealed class SaisieBrut
{
    public FormeBrut Forme { get; set; } = FormeBrut.RondPlein;

    public decimal? Diametre { get; set; }

    public decimal? DiametreInterieur { get; set; }

    public decimal? Largeur { get; set; }

    public decimal? Epaisseur { get; set; }

    public decimal? Cote { get; set; }

    public decimal? SurPlats { get; set; }

    public decimal? Longueur { get; set; }

    public string? DescriptionForme { get; set; }

    public int? MatiereId { get; set; }

    public Unite Unite { get; set; } = Unite.Piece;

    public string? Designation { get; set; }

    public string? Remarques { get; set; }

    public List<SaisieFournisseurBrut> Fournisseurs { get; set; } = [];

    public static SaisieBrut Depuis(FicheBrut fiche)
    {
        ArgumentNullException.ThrowIfNull(fiche);
        return new SaisieBrut
        {
            Forme = fiche.Forme,
            Diametre = fiche.Diametre,
            DiametreInterieur = fiche.DiametreInterieur,
            Largeur = fiche.Largeur,
            Epaisseur = fiche.Epaisseur,
            Cote = fiche.Cote,
            SurPlats = fiche.SurPlats,
            Longueur = fiche.Longueur,
            DescriptionForme = fiche.DescriptionForme,
            MatiereId = fiche.MatiereId,
            Unite = fiche.Unite,
            Designation = fiche.Designation,
            Remarques = fiche.Remarques,
            Fournisseurs = fiche.Fournisseurs.Select(f => new SaisieFournisseurBrut(f.FournisseurId, f.ReferenceFournisseur, f.Principal)).ToList(),
        };
    }
}

/// <summary>Bruts : création, modification, archivage, suppression (règles 5.9).</summary>
public sealed class ServiceBruts(
    IDbContextFactory<AnnuaireDbContext> fabrique,
    IUtilisateurCourant courant,
    ServiceHistorique historique,
    IStockLecture stock)
{
    public async Task<IReadOnlyList<LigneBrut>> Lister(bool inclureArchives = false)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var bruts = await db.Bruts.AsNoTracking()
            .Include(b => b.Matiere)
            .Include(b => b.Fournisseurs).ThenInclude(f => f.Fournisseur)
            .Where(b => inclureArchives || !b.Archive)
            .ToListAsync();
        var usages = await db.PiecesFiniesBruts.GroupBy(pb => pb.BrutId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        return bruts.OrderBy(b => b.Reference, StringComparer.Ordinal).Select(b => VersLigne(b, usages.GetValueOrDefault(b.Id))).ToList();
    }

    internal static LigneBrut VersLigne(Brut b, int nombrePieces)
    {
        var principal = b.Fournisseurs.FirstOrDefault(f => f.Principal);
        var automatique = LibelleBrut.Calculer(b);
        var texte = string.Join(' ',
            new[] { b.Reference, automatique, b.Designation, Format.LibelleForme(b.Forme) }
                .Concat(b.Fournisseurs.SelectMany(f => new[] { f.Fournisseur?.Nom, f.ReferenceFournisseur })));
        return new LigneBrut(b.Id, b.Reference, LibelleBrut.Affiche(b), b.Forme, b.MatiereId, b.Matiere?.Libelle ?? string.Empty, b.Unite,
            principal?.FournisseurId, principal?.Fournisseur?.Nom, b.Fournisseurs.Select(f => f.FournisseurId).ToList(), nombrePieces, b.Archive, texte);
    }

    public async Task<FicheBrut?> Obtenir(int id)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var b = await db.Bruts.AsNoTracking()
            .Include(x => x.Matiere)
            .Include(x => x.Fournisseurs).ThenInclude(f => f.Fournisseur)
            .FirstOrDefaultAsync(x => x.Id == id);
        if (b is null)
        {
            return null;
        }

        var pieces = (await db.PiecesFinies.AsNoTracking()
                .Include(p => p.Client)
                .Include(p => p.Bruts.Where(pb => pb.BrutId == id))
                .Where(p => p.Bruts.Any(pb => pb.BrutId == id))
                .ToListAsync())
            .Select(p => new { pb = p.Bruts[0], p })
            .ToList();
        return new FicheBrut(b.Id, b.Reference, LibelleBrut.Affiche(b), LibelleBrut.Calculer(b), b.Forme, b.Diametre, b.DiametreInterieur,
            b.Largeur, b.Epaisseur, b.Cote, b.SurPlats, b.Longueur, b.DescriptionForme, b.MatiereId, b.Matiere?.Libelle ?? string.Empty, b.Unite,
            b.Designation, b.Remarques, b.Archive,
            b.Fournisseurs.OrderByDescending(f => f.Principal).ThenBy(f => f.Fournisseur?.Nom, StringComparer.Ordinal)
                .Select(f => new FournisseurDuBrut(f.FournisseurId, f.Fournisseur?.Nom ?? "?", f.ReferenceFournisseur, f.Principal)).ToList(),
            pieces.OrderBy(x => x.p.Reference, StringComparer.Ordinal)
                .Select(x => new PieceDuBrut(x.p.Id, x.p.Reference, x.p.Designation, x.p.Client?.Nom ?? string.Empty, x.pb.Principal, x.pb.PiecesParBrut, x.p.Archive)).ToList());
    }

    /// <summary>Prochaine référence interne (affichée dans le formulaire).</summary>
    public async Task<string> ProchaineReference()
    {
        await using var db = await fabrique.CreateDbContextAsync();
        return LibelleBrut.Reference(await ProchainNumero(db));
    }

    public async Task<Resultat<int>> Creer(SaisieBrut saisie)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var erreur = Verifier(saisie);
        if (erreur is not null)
        {
            return Resultat.Echec<int>(erreur);
        }

        await using var db = await fabrique.CreateDbContextAsync();
        if (!await db.Matieres.AnyAsync(m => m.Id == saisie.MatiereId))
        {
            return Resultat.Echec<int>("Choisissez la matière.");
        }

        var brut = new Brut { Reference = LibelleBrut.Reference(await ProchainNumero(db)) };
        Appliquer(brut, saisie);
        db.Bruts.Add(brut);
        await db.SaveChangesAsync();

        await db.Entry(brut).Reference(b => b.Matiere).LoadAsync();
        await historique.Ecrire(AnnuaireExtensions.Cle, TypesObjet.Brut, brut.Id, "creation",
            $"{historique.Sujet} a créé le brut {brut.Reference} ({LibelleBrut.Calculer(brut)})");
        return Resultat.Ok(brut.Id);
    }

    public async Task<Resultat> Modifier(int id, SaisieBrut saisie)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var erreur = Verifier(saisie);
        if (erreur is not null)
        {
            return Resultat.Echec(erreur);
        }

        await using var db = await fabrique.CreateDbContextAsync();
        var brut = await db.Bruts.Include(b => b.Fournisseurs).Include(b => b.Matiere).FirstOrDefaultAsync(b => b.Id == id);
        if (brut is null)
        {
            return Resultat.Echec("Brut introuvable.");
        }

        if (!await db.Matieres.AnyAsync(m => m.Id == saisie.MatiereId))
        {
            return Resultat.Echec("Choisissez la matière.");
        }

        Appliquer(brut, saisie);
        await db.SaveChangesAsync();
        await historique.Ecrire(AnnuaireExtensions.Cle, TypesObjet.Brut, brut.Id, "modification", $"{historique.Sujet} a modifié le brut {brut.Reference}");
        return Resultat.Ok();
    }

    public async Task<Resultat> Archiver(int id, bool archive)
    {
        if (!courant.Peut(Role.Gestion))
        {
            return Resultat.Echec(Resultat.NonAutorise);
        }

        await using var db = await fabrique.CreateDbContextAsync();
        var brut = await db.Bruts.FindAsync(id);
        if (brut is null)
        {
            return Resultat.Echec("Brut introuvable.");
        }

        brut.Archive = archive;
        await db.SaveChangesAsync();
        await historique.Ecrire(AnnuaireExtensions.Cle, TypesObjet.Brut, id, archive ? "archivage" : "desarchivage",
            $"{historique.Sujet} a {(archive ? "archivé" : "désarchivé")} le brut {brut.Reference}");
        return Resultat.Ok();
    }

    /// <summary>Suppression impossible si le brut est utilisé par une pièce finie ou a des mouvements de stock : il faut l'archiver.</summary>
    public async Task<Resultat> Supprimer(int id)
    {
        if (!courant.Peut(Role.Gestion))
        {
            return Resultat.Echec(Resultat.NonAutorise);
        }

        await using var db = await fabrique.CreateDbContextAsync();
        var brut = await db.Bruts.FindAsync(id);
        if (brut is null)
        {
            return Resultat.Echec("Brut introuvable.");
        }

        var pieces = await db.PiecesFiniesBruts.CountAsync(pb => pb.BrutId == id);
        if (pieces > 0)
        {
            return Resultat.Echec($"Ce brut est utilisé par {pieces} pièce(s) finie(s) : il ne peut pas être supprimé. Archivez-le plutôt.");
        }

        if (await stock.ADesMouvements(TypeReference.Brut, id))
        {
            return Resultat.Echec("Ce brut a des mouvements de stock : il ne peut pas être supprimé. Archivez-le plutôt.");
        }

        db.Bruts.Remove(brut);
        await db.SaveChangesAsync();
        await historique.Ecrire(AnnuaireExtensions.Cle, TypesObjet.Brut, id, "suppression", $"{historique.Sujet} a supprimé le brut {brut.Reference}");
        return Resultat.Ok();
    }

    private string? Verifier(SaisieBrut s)
    {
        if (!courant.Peut(Role.Gestion))
        {
            return Resultat.NonAutorise;
        }

        if (s.MatiereId is null)
        {
            return "Choisissez la matière.";
        }

        if (s.Fournisseurs.GroupBy(f => f.FournisseurId).Any(g => g.Count() > 1))
        {
            return "Un même fournisseur est indiqué deux fois.";
        }

        if (s.Fournisseurs.Count(f => f.Principal) > 1)
        {
            return "Un seul fournisseur peut être principal.";
        }

        return LibelleBrut.Verifier(s.Forme, s.Diametre, s.DiametreInterieur, s.Largeur, s.Epaisseur, s.Cote, s.SurPlats, s.Longueur, s.DescriptionForme);
    }

    /// <summary>Recopie la saisie ; seules les dimensions utiles à la forme sont gardées.</summary>
    private static void Appliquer(Brut brut, SaisieBrut s)
    {
        brut.Forme = s.Forme;
        brut.Diametre = s.Forme is FormeBrut.RondPlein or FormeBrut.Tube ? s.Diametre : null;
        brut.DiametreInterieur = s.Forme == FormeBrut.Tube ? s.DiametreInterieur : null;
        brut.Largeur = s.Forme is FormeBrut.Plat or FormeBrut.Tole ? s.Largeur : null;
        brut.Epaisseur = s.Forme is FormeBrut.Plat or FormeBrut.Tole ? s.Epaisseur : null;
        brut.Cote = s.Forme == FormeBrut.Carre ? s.Cote : null;
        brut.SurPlats = s.Forme == FormeBrut.Hexagone ? s.SurPlats : null;
        brut.Longueur = s.Forme is FormeBrut.MouleeForgee or FormeBrut.Autre ? null : s.Longueur;
        brut.DescriptionForme = s.Forme is FormeBrut.MouleeForgee or FormeBrut.Autre ? ServiceReferentiels.Net(s.DescriptionForme) : null;
        brut.MatiereId = s.MatiereId!.Value;
        brut.Unite = s.Unite;
        brut.Designation = ServiceReferentiels.Net(s.Designation);
        brut.Remarques = ServiceReferentiels.Net(s.Remarques);

        brut.Fournisseurs.RemoveAll(f => s.Fournisseurs.All(n => n.FournisseurId != f.FournisseurId));
        foreach (var saisi in s.Fournisseurs)
        {
            var existant = brut.Fournisseurs.FirstOrDefault(f => f.FournisseurId == saisi.FournisseurId);
            if (existant is null)
            {
                existant = new BrutFournisseur { FournisseurId = saisi.FournisseurId };
                brut.Fournisseurs.Add(existant);
            }

            existant.ReferenceFournisseur = ServiceReferentiels.Net(saisi.ReferenceFournisseur);
            existant.Principal = saisi.Principal;
        }
    }

    private static async Task<int> ProchainNumero(AnnuaireDbContext db)
    {
        var references = await db.Bruts.Select(b => b.Reference).ToListAsync();
        return references.Select(LibelleBrut.Numero).DefaultIfEmpty(0).Max() + 1;
    }
}
