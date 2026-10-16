using System.Globalization;
using System.Text;
using Barthazon.Commun.Contrats;
using Barthazon.Commun.Outils;
using Barthazon.Commun.Securite;
using Barthazon.Stock.Donnees;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Stock.Services;

public sealed record BrutDisponible(BrutResume Brut, bool Principal, int PiecesParBrut, decimal Quantite, EtatStock Etat);

public sealed record LignePieceStock(
    PieceFinieResume Piece,
    decimal Quantite,
    decimal? Seuil,
    EtatStock Etat,
    IReadOnlyList<BrutDisponible> Bruts,
    string TexteRecherche)
{
    /// <summary>Somme des stocks des bruts associés.</summary>
    public decimal BrutDisponible => Bruts.Sum(b => b.Quantite);

    /// <summary>Unité affichée pour le brut disponible : celle du brut principal.</summary>
    public Unite UniteBrut => (Bruts.FirstOrDefault(b => b.Principal) ?? Bruts.FirstOrDefault())?.Brut.Unite ?? Unite.Piece;

    public bool PlusDeBrut => BrutDisponible <= 0;
}

public sealed record LigneBrutStock(BrutResume Brut, decimal Quantite, decimal? Seuil, EtatStock Etat, string TexteRecherche);

public sealed record PieceUtilisantBrut(PieceFinieResume Piece, bool Principal, int PiecesParBrut, decimal Quantite, EtatStock Etat);

public sealed record LigneMouvement(
    int Id,
    DateTime Date,
    string Utilisateur,
    TypeMouvement Type,
    MotifSortie? Motif,
    TypeReference TypeReference,
    int ReferenceId,
    string Reference,
    string Designation,
    Unite Unite,
    decimal Quantite,
    decimal QuantiteApres,
    string? Tiers,
    string? NumeroBL,
    string? Commentaire,
    bool Annule,
    int? AnnuleParId,
    int? AnnuleId,
    bool PeutAnnuler)
{
    public string TypeTexte => Motif is { } m ? $"{ReglesStock.Libelle(Type)} · {ReglesStock.Libelle(m)}" : ReglesStock.Libelle(Type);
}

public enum PeriodeJournal
{
    Aujourdhui,
    SeptJours,
    TrenteJours,
    Tout,
}

public sealed class FiltreJournal
{
    public PeriodeJournal Periode { get; set; } = PeriodeJournal.TrenteJours;

    public TypeMouvement? Type { get; set; }

    public TypeReference? TypeReference { get; set; }

    public int? ReferenceId { get; set; }

    public int? UtilisateurId { get; set; }

    public string? Texte { get; set; }
}

public sealed record Alerte(TypeReference Type, int Id, string Reference, string Libelle, decimal Quantite, decimal? Seuil, Unite Unite, EtatStock Etat, bool PlusDeBrut);

/// <summary>Consultation du stock : listes, fiches, journal, alertes. Toutes les références de l'Annuaire y figurent, à 0 tant qu'aucun mouvement n'a été saisi.</summary>
public sealed class ServiceStock(
    IDbContextFactory<StockDbContext> fabrique,
    IAnnuaireLecture annuaire,
    ServiceUtilisateurs utilisateurs,
    ServiceMouvements mouvements)
{
    public async Task<IReadOnlyList<LignePieceStock>> ListerPieces(bool inclureArchivees = false)
    {
        var niveaux = await Niveaux();
        var pieces = await annuaire.ListerPiecesFinies(inclureArchivees: true);
        var associations = (await annuaire.ListerAssociations()).ToLookup(a => a.PieceFinieId);
        return pieces
            .Select(p => VersLigne(p, niveaux, associations[p.Id]))
            .Where(l => inclureArchivees || !l.Piece.Archive || l.Quantite != 0)
            .ToList();
    }

    public async Task<LignePieceStock?> ObtenirPiece(int id)
    {
        var piece = await annuaire.ObtenirPieceFinie(id);
        return piece is null ? null : VersLigne(piece, await Niveaux(), await annuaire.BrutsDeLaPiece(id));
    }

    public async Task<IReadOnlyList<LigneBrutStock>> ListerBruts(bool inclureArchives = false)
    {
        var niveaux = await Niveaux();
        return (await annuaire.ListerBruts(inclureArchives: true))
            .Select(b => VersLigne(b, niveaux))
            .Where(l => inclureArchives || !l.Brut.Archive || l.Quantite != 0)
            .ToList();
    }

    public async Task<LigneBrutStock?> ObtenirBrut(int id)
    {
        var brut = await annuaire.ObtenirBrut(id);
        return brut is null ? null : VersLigne(brut, await Niveaux());
    }

    /// <summary>Pièces finies qui utilisent un brut, avec leur stock.</summary>
    public async Task<IReadOnlyList<PieceUtilisantBrut>> PiecesDuBrut(int brutId)
    {
        var niveaux = await Niveaux();
        var associations = (await annuaire.ListerAssociations()).Where(a => a.Brut.Id == brutId).ToList();
        var pieces = (await annuaire.ListerPiecesFinies(inclureArchivees: true)).ToDictionary(p => p.Id);
        return associations
            .Where(a => pieces.ContainsKey(a.PieceFinieId))
            .Select(a =>
            {
                var niveau = niveaux.GetValueOrDefault((TypeReference.PieceFinie, a.PieceFinieId));
                return new PieceUtilisantBrut(pieces[a.PieceFinieId], a.Principal, a.PiecesParBrut, niveau?.Quantite ?? 0, ReglesStock.Etat(niveau));
            })
            .OrderBy(p => p.Piece.Reference, StringComparer.Ordinal)
            .ToList();
    }

    public async Task<NiveauStock?> Niveau(TypeReference type, int id)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        return await db.Niveaux.AsNoTracking().FirstOrDefaultAsync(n => n.TypeReference == type && n.ReferenceId == id);
    }

    /// <summary>Alertes : ruptures d'abord, puis sous le seuil.</summary>
    public async Task<IReadOnlyList<Alerte>> Alertes()
    {
        var alertes = new List<Alerte>();
        foreach (var p in await ListerPieces())
        {
            if (p.Etat is EtatStock.Rupture or EtatStock.SousLeSeuil)
            {
                alertes.Add(new Alerte(TypeReference.PieceFinie, p.Piece.Id, p.Piece.Reference, p.Piece.Designation, p.Quantite, p.Seuil, Unite.Piece, p.Etat, p.PlusDeBrut));
            }
        }

        foreach (var b in await ListerBruts())
        {
            if (b.Etat is EtatStock.Rupture or EtatStock.SousLeSeuil)
            {
                alertes.Add(new Alerte(TypeReference.Brut, b.Brut.Id, b.Brut.Reference, b.Brut.Libelle, b.Quantite, b.Seuil, b.Brut.Unite, b.Etat, false));
            }
        }

        return alertes
            .OrderByDescending(a => a.Etat == EtatStock.Rupture)
            .ThenBy(a => a.Type)
            .ThenBy(a => a.Reference, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Journal des mouvements, du plus récent au plus ancien.</summary>
    public async Task<IReadOnlyList<LigneMouvement>> Journal(FiltreJournal filtre, int maximum = 1000)
    {
        ArgumentNullException.ThrowIfNull(filtre);
        await using var db = await fabrique.CreateDbContextAsync();
        var requete = db.Mouvements.AsNoTracking();
        var depuis = filtre.Periode switch
        {
            PeriodeJournal.Aujourdhui => DateTime.Today,
            PeriodeJournal.SeptJours => DateTime.Today.AddDays(-6),
            PeriodeJournal.TrenteJours => DateTime.Today.AddDays(-29),
            _ => (DateTime?)null,
        };
        if (depuis is not null)
        {
            requete = requete.Where(m => m.Date >= depuis);
        }

        if (filtre.Type is { } type)
        {
            requete = requete.Where(m => m.TypeMouvement == type);
        }

        if (filtre.TypeReference is { } typeReference)
        {
            requete = requete.Where(m => m.TypeReference == typeReference);
        }

        if (filtre.ReferenceId is { } referenceId)
        {
            requete = requete.Where(m => m.ReferenceId == referenceId);
        }

        if (filtre.UtilisateurId is { } utilisateur)
        {
            requete = requete.Where(m => m.UtilisateurId == utilisateur);
        }

        var liste = await requete.OrderByDescending(m => m.Date).ThenByDescending(m => m.Id).ToListAsync();
        var lignes = await Decrire(liste);
        return lignes.Where(l => Recherche.Correspond(filtre.Texte, l.Reference, l.Designation, l.Utilisateur, l.Tiers, l.NumeroBL, l.Commentaire, l.TypeTexte))
            .Take(maximum)
            .ToList();
    }

    public Task<IReadOnlyList<LigneMouvement>> DerniersMouvements(TypeReference type, int id, int nombre = 8) =>
        Journal(new FiltreJournal { Periode = PeriodeJournal.Tout, TypeReference = type, ReferenceId = id }, nombre);

    /// <summary>Opération complète d'un mouvement (ex. les deux mouvements d'une fabrication).</summary>
    public async Task<IReadOnlyList<LigneMouvement>> Operation(int mouvementId)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var operation = await db.Mouvements.Where(m => m.Id == mouvementId).Select(m => m.OperationId).FirstOrDefaultAsync();
        return await Decrire(await db.Mouvements.AsNoTracking().Where(m => m.OperationId == operation).OrderBy(m => m.Id).ToListAsync());
    }

    /// <summary>Export CSV (séparateur « ; », UTF-8 avec BOM : s'ouvre directement dans Excel).</summary>
    public static byte[] Csv(IEnumerable<LigneMouvement> lignes)
    {
        ArgumentNullException.ThrowIfNull(lignes);
        static string Champ(string? valeur) =>
            valeur is null ? string.Empty : valeur.Contains(';', StringComparison.Ordinal) || valeur.Contains('"', StringComparison.Ordinal) || valeur.Contains('\n', StringComparison.Ordinal)
                ? "\"" + valeur.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
                : valeur;
        static string Nombre(decimal valeur) => valeur.ToString("0.###", Format.Culture);

        var texte = new StringBuilder();
        texte.AppendLine("Date;Utilisateur;Type;Sorte;Référence;Désignation;Quantité;Unité;Stock après;Fournisseur / client;N° BL;Commentaire;Annulé");
        foreach (var l in lignes)
        {
            texte.AppendJoin(';',
                l.Date.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
                Champ(l.Utilisateur),
                Champ(l.TypeTexte),
                l.TypeReference == TypeReference.Brut ? "Brut" : "Pièce finie",
                Champ(l.Reference),
                Champ(l.Designation),
                Nombre(l.Quantite),
                Format.Unite(l.Unite, 2),
                Nombre(l.QuantiteApres),
                Champ(l.Tiers),
                Champ(l.NumeroBL),
                Champ(l.Commentaire),
                l.Annule ? "oui" : string.Empty);
            texte.AppendLine();
        }

        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(texte.ToString())];
    }

    private async Task<List<LigneMouvement>> Decrire(List<Mouvement> liste)
    {
        var prenoms = await utilisateurs.Prenoms();
        var pieces = (await annuaire.ListerPiecesFinies(inclureArchivees: true)).ToDictionary(p => p.Id);
        var bruts = (await annuaire.ListerBruts(inclureArchives: true)).ToDictionary(b => b.Id);
        var clients = (await annuaire.ListerClients()).ToDictionary(c => c.Id, c => c.Nom);
        var fournisseurs = (await annuaire.ListerFournisseurs()).ToDictionary(f => f.Id, f => f.Nom);
        return liste.Select(m =>
        {
            string reference, designation;
            var unite = Unite.Piece;
            if (m.TypeReference == TypeReference.Brut && bruts.GetValueOrDefault(m.ReferenceId) is { } brut)
            {
                (reference, designation, unite) = (brut.Reference, brut.Libelle, brut.Unite);
            }
            else if (m.TypeReference == TypeReference.PieceFinie && pieces.GetValueOrDefault(m.ReferenceId) is { } piece)
            {
                (reference, designation) = (piece.Reference, piece.Designation);
            }
            else
            {
                (reference, designation) = ($"#{m.ReferenceId}", "(supprimé de l'annuaire)");
            }

            var tiers = m.FournisseurId is { } f ? fournisseurs.GetValueOrDefault(f) : m.ClientId is { } c ? clients.GetValueOrDefault(c) : null;
            return new LigneMouvement(m.Id, m.Date, prenoms.GetValueOrDefault(m.UtilisateurId, "?"), m.TypeMouvement, m.Motif, m.TypeReference, m.ReferenceId,
                reference, designation, unite, m.Quantite, m.QuantiteApres, tiers, m.NumeroBL, m.Commentaire, m.AnnuleParId is not null,
                m.AnnuleParId, m.AnnuleId, mouvements.PeutAnnuler(m));
        }).ToList();
    }

    private async Task<Dictionary<(TypeReference, int), NiveauStock>> Niveaux()
    {
        await using var db = await fabrique.CreateDbContextAsync();
        return await db.Niveaux.AsNoTracking().ToDictionaryAsync(n => (n.TypeReference, n.ReferenceId));
    }

    private static LignePieceStock VersLigne(PieceFinieResume p, Dictionary<(TypeReference, int), NiveauStock> niveaux, IEnumerable<BrutAssocie> associations)
    {
        var niveau = niveaux.GetValueOrDefault((TypeReference.PieceFinie, p.Id));
        var bruts = associations
            .OrderByDescending(a => a.Principal).ThenBy(a => a.Brut.Reference, StringComparer.Ordinal)
            .Select(a =>
            {
                var n = niveaux.GetValueOrDefault((TypeReference.Brut, a.Brut.Id));
                return new BrutDisponible(a.Brut, a.Principal, a.PiecesParBrut, n?.Quantite ?? 0, ReglesStock.Etat(n));
            })
            .ToList();
        var texte = string.Join(' ', new[] { p.Reference, p.Designation, p.Client }.Concat(bruts.Select(b => $"{b.Brut.Reference} {b.Brut.Libelle}")));
        return new LignePieceStock(p, niveau?.Quantite ?? 0, niveau?.SeuilAlerte, ReglesStock.Etat(niveau), bruts, texte);
    }

    private static LigneBrutStock VersLigne(BrutResume b, Dictionary<(TypeReference, int), NiveauStock> niveaux)
    {
        var niveau = niveaux.GetValueOrDefault((TypeReference.Brut, b.Id));
        return new LigneBrutStock(b, niveau?.Quantite ?? 0, niveau?.SeuilAlerte, ReglesStock.Etat(niveau),
            $"{b.Reference} {b.Libelle} {b.Matiere} {b.FournisseurPrincipal} {Format.LibelleForme(b.Forme)}");
    }
}

/// <summary>Contrat de lecture du Stock, utilisé par l'Annuaire (aperçus, interdiction de suppression, cloche).</summary>
internal sealed class StockLecture(IDbContextFactory<StockDbContext> fabrique, ServiceStock stock, IAnnuaireLecture annuaire) : IStockLecture
{
    public async Task<ApercuStock?> ApercuPieceFinie(int pieceFinieId)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var niveau = await db.Niveaux.AsNoTracking().FirstOrDefaultAsync(n => n.TypeReference == TypeReference.PieceFinie && n.ReferenceId == pieceFinieId);
        var dernier = await db.Mouvements.AsNoTracking()
            .Where(m => m.TypeReference == TypeReference.PieceFinie && m.ReferenceId == pieceFinieId)
            .OrderByDescending(m => m.Date).ThenByDescending(m => m.Id)
            .FirstOrDefaultAsync();
        var quantite = niveau?.Quantite ?? 0;
        return new ApercuStock(quantite, Format.Quantite(quantite, Unite.Piece), ReglesStock.Etat(niveau), niveau?.SeuilAlerte, dernier?.Date,
            dernier is null ? "Aucun mouvement" : $"Dernier mouvement : {Format.DateCourte(dernier.Date)} {ReglesStock.Libelle(dernier.TypeMouvement).ToLowerInvariant()} {Format.QuantiteSignee(dernier.Quantite, Unite.Piece)}");
    }

    public async Task<ApercuStock?> ApercuBrut(int brutId)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var niveau = await db.Niveaux.AsNoTracking().FirstOrDefaultAsync(n => n.TypeReference == TypeReference.Brut && n.ReferenceId == brutId);
        var arrivage = await db.Mouvements.AsNoTracking()
            .Where(m => m.TypeReference == TypeReference.Brut && m.ReferenceId == brutId && m.TypeMouvement == TypeMouvement.Arrivage && m.AnnuleParId == null)
            .OrderByDescending(m => m.Date).ThenByDescending(m => m.Id)
            .FirstOrDefaultAsync();
        var unite = (await annuaire.ObtenirBrut(brutId))?.Unite ?? Unite.Piece;
        var quantite = niveau?.Quantite ?? 0;
        return new ApercuStock(quantite, Format.Quantite(quantite, unite), ReglesStock.Etat(niveau), niveau?.SeuilAlerte, arrivage?.Date,
            arrivage is null ? "Aucun arrivage" : $"Dernier arrivage : {Format.DateCourte(arrivage.Date)} {Format.QuantiteSignee(arrivage.Quantite, unite)}");
    }

    public async Task<bool> ADesMouvements(TypeReference type, int id)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        return await db.Mouvements.AnyAsync(m => m.TypeReference == type && m.ReferenceId == id);
    }

    public async Task<int> NombreAlertes() => (await stock.Alertes()).Count;
}
