using Barthazon.Annuaire.Donnees;
using Barthazon.Commun.Contrats;
using Barthazon.Commun.Outils;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Annuaire.Services;

/// <summary>Contrat de lecture de l'Annuaire, utilisé par le Stock.</summary>
internal sealed class AnnuaireLecture(IDbContextFactory<AnnuaireDbContext> fabrique) : IAnnuaireLecture
{
    public async Task<IReadOnlyList<PieceFinieResume>> ListerPiecesFinies(bool inclureArchivees = false)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var indices = await db.Indices.AsNoTracking().ToDictionaryAsync(i => i.Id, i => i.Code);
        return (await db.PiecesFinies.AsNoTracking().Include(p => p.Client).Where(p => inclureArchivees || !p.Archive).ToListAsync())
            .OrderBy(p => p.Reference, StringComparer.Create(Format.Culture, true))
            .Select(p => VersResume(p, indices))
            .ToList();
    }

    public async Task<PieceFinieResume?> ObtenirPieceFinie(int id)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var piece = await db.PiecesFinies.AsNoTracking().Include(p => p.Client).FirstOrDefaultAsync(p => p.Id == id);
        if (piece is null)
        {
            return null;
        }

        var indices = await db.Indices.AsNoTracking().Where(i => i.Id == piece.IndiceCourantId).ToDictionaryAsync(i => i.Id, i => i.Code);
        return VersResume(piece, indices);
    }

    public async Task<IReadOnlyList<BrutResume>> ListerBruts(bool inclureArchives = false)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        return (await RequeteBruts(db).Where(b => inclureArchives || !b.Archive).ToListAsync())
            .OrderBy(b => b.Reference, StringComparer.Ordinal)
            .Select(VersResume)
            .ToList();
    }

    public async Task<BrutResume?> ObtenirBrut(int id)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var brut = await RequeteBruts(db).FirstOrDefaultAsync(b => b.Id == id);
        return brut is null ? null : VersResume(brut);
    }

    public async Task<IReadOnlyList<BrutAssocie>> BrutsDeLaPiece(int pieceFinieId) =>
        (await Associations(pb => pb.PieceFinieId == pieceFinieId)).OrderByDescending(a => a.Principal).ThenBy(a => a.Brut.Reference, StringComparer.Ordinal).ToList();

    public async Task<IReadOnlyList<BrutAssocie>> ListerAssociations() => await Associations(_ => true);

    public async Task<IReadOnlyList<FournisseurBrut>> FournisseursDuBrut(int brutId)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        return (await db.BrutsFournisseurs.AsNoTracking().Include(f => f.Fournisseur).Where(f => f.BrutId == brutId).ToListAsync())
            .OrderByDescending(f => f.Principal).ThenBy(f => f.Fournisseur?.Nom, StringComparer.Ordinal)
            .Select(f => new FournisseurBrut(f.FournisseurId, f.Fournisseur?.Nom ?? "?", f.ReferenceFournisseur, f.Principal))
            .ToList();
    }

    public async Task<IReadOnlyList<TiersResume>> ListerClients()
    {
        await using var db = await fabrique.CreateDbContextAsync();
        return (await db.Clients.AsNoTracking().ToListAsync()).OrderBy(c => c.Nom, StringComparer.Create(Format.Culture, true)).Select(c => new TiersResume(c.Id, c.Nom)).ToList();
    }

    public async Task<IReadOnlyList<TiersResume>> ListerFournisseurs()
    {
        await using var db = await fabrique.CreateDbContextAsync();
        return (await db.Fournisseurs.AsNoTracking().ToListAsync()).OrderBy(f => f.Nom, StringComparer.Create(Format.Culture, true)).Select(f => new TiersResume(f.Id, f.Nom)).ToList();
    }

    private async Task<List<BrutAssocie>> Associations(System.Linq.Expressions.Expression<Func<PieceFinieBrut, bool>> filtre)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var associations = await db.PiecesFiniesBruts.AsNoTracking().Where(filtre).ToListAsync();
        var ids = associations.Select(a => a.BrutId).Distinct().ToList();
        var bruts = (await RequeteBruts(db).Where(b => ids.Contains(b.Id)).ToListAsync()).ToDictionary(b => b.Id, VersResume);
        return associations
            .Where(a => bruts.ContainsKey(a.BrutId))
            .Select(a => new BrutAssocie(a.PieceFinieId, bruts[a.BrutId], a.Principal, a.PiecesParBrut))
            .ToList();
    }

    private static IQueryable<Brut> RequeteBruts(AnnuaireDbContext db) =>
        db.Bruts.AsNoTracking().Include(b => b.Matiere).Include(b => b.Fournisseurs).ThenInclude(f => f.Fournisseur);

    private static BrutResume VersResume(Brut b)
    {
        var principal = b.Fournisseurs.FirstOrDefault(f => f.Principal);
        return new BrutResume(b.Id, b.Reference, LibelleBrut.Affiche(b), b.Forme, b.MatiereId, b.Matiere?.Libelle ?? string.Empty, b.Unite,
            principal?.FournisseurId, principal?.Fournisseur?.Nom, b.Archive);
    }

    private static PieceFinieResume VersResume(PieceFinie p, Dictionary<int, string> indices) =>
        new(p.Id, p.Reference, p.Designation, p.ClientId, p.Client?.Nom ?? string.Empty,
            p.IndiceCourantId is { } id ? indices.GetValueOrDefault(id, "?") : "?", p.Archive);
}

/// <summary>Résultats de la recherche globale : pièces finies, bruts, clients, fournisseurs.</summary>
internal sealed class RechercheAnnuaire(ServicePiecesFinies pieces, ServiceBruts bruts, ServiceReferentiels referentiels) : IRechercheGlobale
{
    public async Task<IReadOnlyList<ResultatRecherche>> Rechercher(string texte, int maximumParGroupe)
    {
        var resultats = new List<ResultatRecherche>();
        resultats.AddRange((await pieces.Lister()).Where(p => Recherche.Correspond(texte, p.TexteRecherche)).Take(maximumParGroupe)
            .Select(p => new ResultatRecherche("Pièces finies", 1, TypesObjet.PieceFinie, p.Id, $"{p.Reference} · {p.Designation}", p.Client)));
        resultats.AddRange((await bruts.Lister()).Where(b => Recherche.Correspond(texte, b.TexteRecherche)).Take(maximumParGroupe)
            .Select(b => new ResultatRecherche("Bruts", 2, TypesObjet.Brut, b.Id, $"{b.Reference} · {b.Libelle}", b.FournisseurPrincipal)));
        resultats.AddRange((await referentiels.ListerTiers(SorteTiers.Client)).Where(c => Recherche.Correspond(texte, c.Nom, c.Contact)).Take(maximumParGroupe)
            .Select(c => new ResultatRecherche("Clients", 3, TypesObjet.Client, c.Id, c.Nom, c.Contact)));
        resultats.AddRange((await referentiels.ListerTiers(SorteTiers.Fournisseur)).Where(f => Recherche.Correspond(texte, f.Nom, f.Contact)).Take(maximumParGroupe)
            .Select(f => new ResultatRecherche("Fournisseurs", 4, TypesObjet.Fournisseur, f.Id, f.Nom, f.Contact)));
        return resultats;
    }
}
