using Barthazon.Annuaire.Donnees;
using Barthazon.Commun.Contrats;
using Barthazon.Commun.Historique;
using Barthazon.Commun.Outils;
using Barthazon.Commun.Securite;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Annuaire.Services;

public sealed record LignePiece(
    int Id,
    string Reference,
    string Designation,
    int ClientId,
    string Client,
    string Indice,
    int? BrutPrincipalId,
    string? BrutPrincipalReference,
    string? BrutPrincipal,
    bool Archive,
    string TexteRecherche);

public sealed record BrutDePiece(int BrutId, string Reference, string Libelle, Unite Unite, bool Principal, int PiecesParBrut, bool Archive);

public sealed record IndiceResume(int Id, string Code, DateTime Date, string? Auteur, string? DescriptionChangement, bool Courant);

public sealed record PlanAffiche(int Id, string NomFichier, string TypeMime, long Taille, DateTime DateAjout, string? Auteur, int IndiceId, string Indice, bool AncienIndice)
{
    public bool EstImage => TypeMime.StartsWith("image/", StringComparison.Ordinal);
}

public sealed record OperationAffichee(int Id, int Numero, int? PosteId, string? Poste, string Description, int? TempsEstimeMin, string? Remarque);

public sealed record LigneSpec(string Libelle, string Valeur);

public sealed record SpecificationAffichee(
    string? TraitementThermique,
    string? TraitementSurface,
    string? ToleranceGenerale,
    string? Masse,
    string? Remarques,
    IReadOnlyList<LigneSpec> Lignes)
{
    public bool EstVide => TraitementThermique is null && TraitementSurface is null && ToleranceGenerale is null
        && Masse is null && Remarques is null && Lignes.Count == 0;
}

public sealed record DetailIndice(
    int Id,
    string Code,
    DateTime Date,
    string? Auteur,
    string? DescriptionChangement,
    bool Courant,
    SpecificationAffichee Specification,
    IReadOnlyList<PlanAffiche> Plans,
    IReadOnlyList<OperationAffichee> Operations);

/// <param name="Entrant">Lien créé depuis l'autre pièce (« Même famille », « Se monte avec »).</param>
public sealed record LienAffiche(int Id, TypeLien Type, int? PieceCibleId, string? PieceCibleReference, string? PieceCibleDesignation, string? Url, string? Libelle, bool Entrant);

public sealed record FichePiece(
    int Id,
    string Reference,
    string Designation,
    int ClientId,
    string Client,
    string? EmplacementPlanPapier,
    string? Remarques,
    bool Archive,
    DetailIndice IndiceCourant,
    IReadOnlyList<IndiceResume> Indices,
    IReadOnlyList<BrutDePiece> Bruts,
    IReadOnlyList<PlanAffiche> Plans,
    IReadOnlyList<LienAffiche> Liens)
{
    public BrutDePiece? BrutPrincipal => Bruts.FirstOrDefault(b => b.Principal) ?? Bruts.FirstOrDefault();
}

public sealed record BrutChoisi(int BrutId, bool Principal, int PiecesParBrut);

public sealed class SaisieSpecification
{
    public string? TraitementThermique { get; set; }

    public string? TraitementSurface { get; set; }

    public string? ToleranceGenerale { get; set; }

    public string? Masse { get; set; }

    public string? Remarques { get; set; }

    public List<LigneSaisie> Lignes { get; set; } = [];

    public static SaisieSpecification Depuis(SpecificationAffichee s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new()
        {
            TraitementThermique = s.TraitementThermique,
            TraitementSurface = s.TraitementSurface,
            ToleranceGenerale = s.ToleranceGenerale,
            Masse = s.Masse,
            Remarques = s.Remarques,
            Lignes = s.Lignes.Select(l => new LigneSaisie { Libelle = l.Libelle, Valeur = l.Valeur }).ToList(),
        };
    }

    public sealed class LigneSaisie
    {
        public string Libelle { get; set; } = string.Empty;

        public string Valeur { get; set; } = string.Empty;
    }
}

public sealed class SaisieCreationPiece
{
    public string Reference { get; set; } = string.Empty;

    public string Designation { get; set; } = string.Empty;

    public int? ClientId { get; set; }

    public string Indice { get; set; } = "A";

    public List<BrutChoisi> Bruts { get; set; } = [];

    public string? EmplacementPlanPapier { get; set; }

    public SaisieSpecification? Specification { get; set; }
}

public sealed record SaisieIdentite(string Reference, string Designation, int ClientId, string? EmplacementPlanPapier, string? Remarques);

public sealed class SaisieOperation
{
    public int? Numero { get; set; }

    public int? PosteId { get; set; }

    public string Description { get; set; } = string.Empty;

    public int? TempsEstimeMin { get; set; }

    public string? Remarque { get; set; }
}

/// <summary>Pièces finies : bruts associés, indices, spécifications, plans, gamme, liens (règles 5.9).</summary>
public sealed class ServicePiecesFinies(
    IDbContextFactory<AnnuaireDbContext> fabrique,
    IUtilisateurCourant courant,
    ServiceHistorique historique,
    ServiceUtilisateurs utilisateurs,
    StockagePlans stockage,
    IStockLecture stock,
    TimeProvider horloge)
{
    private const string Module = AnnuaireExtensions.Cle;
    private const string Type = TypesObjet.PieceFinie;

    // ---------- Lecture ----------

    public async Task<IReadOnlyList<LignePiece>> Lister(bool inclureArchivees = false)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var pieces = await db.PiecesFinies.AsNoTracking()
            .Include(p => p.Client)
            .Include(p => p.Bruts).ThenInclude(pb => pb.Brut).ThenInclude(b => b!.Matiere)
            .Where(p => inclureArchivees || !p.Archive)
            .ToListAsync();
        var indices = await db.Indices.AsNoTracking().ToDictionaryAsync(i => i.Id, i => i.Code);
        return pieces
            .OrderBy(p => p.Reference, StringComparer.Create(Format.Culture, true))
            .Select(p =>
            {
                var principal = (p.Bruts.FirstOrDefault(b => b.Principal) ?? p.Bruts.FirstOrDefault())?.Brut;
                var indice = p.IndiceCourantId is { } id ? indices.GetValueOrDefault(id, "?") : "?";
                var texte = string.Join(' ', new[] { p.Reference, p.Designation, p.Client?.Nom }
                    .Concat(p.Bruts.Select(b => b.Brut is null ? null : $"{b.Brut.Reference} {LibelleBrut.Calculer(b.Brut)}")));
                return new LignePiece(p.Id, p.Reference, p.Designation, p.ClientId, p.Client?.Nom ?? string.Empty, indice,
                    principal?.Id, principal?.Reference, principal is null ? null : LibelleBrut.Affiche(principal), p.Archive, texte);
            })
            .ToList();
    }

    public async Task<FichePiece?> Obtenir(int id)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var piece = await db.PiecesFinies.AsNoTracking()
            .Include(p => p.Client)
            .Include(p => p.Bruts).ThenInclude(pb => pb.Brut).ThenInclude(b => b!.Matiere)
            .Include(p => p.Indices).ThenInclude(i => i.Plans)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id);
        if (piece is null)
        {
            return null;
        }

        var prenoms = await utilisateurs.Prenoms();
        var courantId = piece.IndiceCourantId ?? piece.Indices.OrderBy(i => i.Id).Last().Id;
        var detail = await ChargerIndice(db, courantId, courantId, prenoms);

        var indicesTries = piece.Indices.OrderByDescending(i => i.Id).ToList();
        var plans = indicesTries
            .SelectMany(i => i.Plans.OrderByDescending(p => p.DateAjout).Select(p => VersPlan(p, i, i.Id != courantId, prenoms)))
            .ToList();

        var liens = await db.Liens.AsNoTracking().Where(l => l.PieceFinieId == id || l.PieceCibleId == id).ToListAsync();
        var autresIds = liens.Select(l => l.PieceFinieId == id ? l.PieceCibleId : l.PieceFinieId).OfType<int>().Distinct().ToList();
        var autres = await db.PiecesFinies.AsNoTracking().Where(p => autresIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);
        var liensAffiches = liens
            .Where(l => l.PieceFinieId == id || l.Type is TypeLien.MemeFamille or TypeLien.SeMonteAvec)
            .Select(l =>
            {
                var entrant = l.PieceFinieId != id;
                var cibleId = entrant ? l.PieceFinieId : l.PieceCibleId;
                var cible = cibleId is { } c ? autres.GetValueOrDefault(c) : null;
                return new LienAffiche(l.Id, l.Type, cibleId, cible?.Reference, cible?.Designation, l.Url, l.Libelle, entrant);
            })
            .OrderBy(l => l.Type)
            .ThenBy(l => l.PieceCibleReference ?? l.Libelle, StringComparer.Ordinal)
            .ToList();

        return new FichePiece(piece.Id, piece.Reference, piece.Designation, piece.ClientId, piece.Client?.Nom ?? string.Empty,
            piece.EmplacementPlanPapier, piece.Remarques, piece.Archive, detail!,
            indicesTries.Select(i => new IndiceResume(i.Id, i.Code, i.Date, Prenom(prenoms, i.AuteurId), i.DescriptionChangement, i.Id == courantId)).ToList(),
            piece.Bruts.Where(b => b.Brut is not null)
                .OrderByDescending(b => b.Principal).ThenBy(b => b.Brut!.Reference, StringComparer.Ordinal)
                .Select(b => new BrutDePiece(b.BrutId, b.Brut!.Reference, LibelleBrut.Affiche(b.Brut), b.Brut.Unite, b.Principal, b.PiecesParBrut, b.Brut.Archive))
                .ToList(),
            plans,
            liensAffiches);
    }

    /// <summary>Détail d'un indice (consultation en lecture seule des indices précédents).</summary>
    public async Task<DetailIndice?> ObtenirIndice(int indiceId)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var indice = await db.Indices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == indiceId);
        if (indice is null)
        {
            return null;
        }

        var courantId = await db.PiecesFinies.Where(p => p.Id == indice.PieceFinieId).Select(p => p.IndiceCourantId).FirstOrDefaultAsync();
        return await ChargerIndice(db, indiceId, courantId ?? indiceId, await utilisateurs.Prenoms());
    }

    private static async Task<DetailIndice?> ChargerIndice(AnnuaireDbContext db, int indiceId, int courantId, IReadOnlyDictionary<int, string> prenoms)
    {
        var i = await db.Indices.AsNoTracking()
            .Include(x => x.Specification)
            .Include(x => x.LignesSpecification)
            .Include(x => x.Plans)
            .Include(x => x.Operations).ThenInclude(o => o.Poste)
            .AsSplitQuery()
            .FirstOrDefaultAsync(x => x.Id == indiceId);
        if (i is null)
        {
            return null;
        }

        var s = i.Specification;
        var specification = new SpecificationAffichee(s?.TraitementThermique, s?.TraitementSurface, s?.ToleranceGenerale, s?.Masse, s?.Remarques,
            i.LignesSpecification.OrderBy(l => l.Ordre).Select(l => new LigneSpec(l.Libelle, l.Valeur)).ToList());
        return new DetailIndice(i.Id, i.Code, i.Date, Prenom(prenoms, i.AuteurId), i.DescriptionChangement, i.Id == courantId, specification,
            i.Plans.OrderByDescending(p => p.DateAjout).Select(p => VersPlan(p, i, i.Id != courantId, prenoms)).ToList(),
            i.Operations.OrderBy(o => o.Ordre).ThenBy(o => o.Numero)
                .Select(o => new OperationAffichee(o.Id, o.Numero, o.PosteId, o.Poste?.Libelle, o.Description, o.TempsEstimeMin, o.Remarque)).ToList());
    }

    private static PlanAffiche VersPlan(Plan p, Indice i, bool ancien, IReadOnlyDictionary<int, string> prenoms) =>
        new(p.Id, p.NomFichier, p.TypeMime, p.Taille, p.DateAjout, Prenom(prenoms, p.AuteurId), i.Id, i.Code, ancien);

    private static string? Prenom(IReadOnlyDictionary<int, string> prenoms, int? id) => id is { } v ? prenoms.GetValueOrDefault(v) : null;

    /// <summary>Fichier d'un plan pour l'ouvrir ou le télécharger.</summary>
    public async Task<(string Chemin, string Nom, string Type)?> FichierPlan(int planId)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var plan = await db.Plans.AsNoTracking().FirstOrDefaultAsync(p => p.Id == planId);
        var chemin = plan is null ? null : stockage.CheminComplet(plan.Chemin);
        return chemin is null || !File.Exists(chemin) ? null : (chemin, plan!.NomFichier, plan.TypeMime);
    }

    // ---------- Création, identité, archivage ----------

    public async Task<Resultat<int>> Creer(SaisieCreationPiece saisie)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        if (!courant.Peut(Role.Gestion))
        {
            return Resultat.Echec<int>(Resultat.NonAutorise);
        }

        var reference = saisie.Reference?.Trim() ?? string.Empty;
        var designation = saisie.Designation?.Trim() ?? string.Empty;
        var code = saisie.Indice?.Trim() ?? string.Empty;
        if (reference.Length == 0 || designation.Length == 0)
        {
            return Resultat.Echec<int>("La référence et la désignation sont obligatoires.");
        }

        if (saisie.ClientId is null)
        {
            return Resultat.Echec<int>("Choisissez le client.");
        }

        if (code.Length == 0)
        {
            return Resultat.Echec<int>("Indiquez l'indice (A par défaut).");
        }

        if (saisie.Bruts.Count == 0)
        {
            return Resultat.Echec<int>("Choisissez au moins un brut : une pièce finie ne peut pas être enregistrée sans brut.");
        }

        if (saisie.Bruts.Any(b => b.PiecesParBrut < 1))
        {
            return Resultat.Echec<int>("Le nombre de pièces obtenues par brut doit être au moins 1.");
        }

        await using var db = await fabrique.CreateDbContextAsync();
        var client = await db.Clients.FindAsync(saisie.ClientId.Value);
        if (client is null)
        {
            return Resultat.Echec<int>("Client introuvable.");
        }

        if (await db.PiecesFinies.AnyAsync(p => p.ClientId == client.Id && p.Reference == reference))
        {
            return Resultat.Echec<int>($"La pièce {reference} existe déjà pour le client {client.Nom}.");
        }

        var brutsIds = saisie.Bruts.Select(b => b.BrutId).Distinct().ToList();
        if (await db.Bruts.CountAsync(b => brutsIds.Contains(b.Id)) != brutsIds.Count)
        {
            return Resultat.Echec<int>("Un des bruts choisis n'existe plus.");
        }

        var principal = saisie.Bruts.FirstOrDefault(b => b.Principal)?.BrutId ?? saisie.Bruts[0].BrutId;
        var piece = new PieceFinie
        {
            Reference = reference,
            Designation = designation,
            ClientId = client.Id,
            EmplacementPlanPapier = ServiceReferentiels.Net(saisie.EmplacementPlanPapier),
            Bruts = brutsIds.Select(id => new PieceFinieBrut
            {
                BrutId = id,
                Principal = id == principal,
                PiecesParBrut = saisie.Bruts.First(b => b.BrutId == id).PiecesParBrut,
            }).ToList(),
        };
        var indice = new Indice { Code = code, Date = Maintenant(), AuteurId = Auteur(), DescriptionChangement = "Création" };
        if (saisie.Specification is not null)
        {
            AppliquerSpecification(indice, saisie.Specification);
        }

        piece.Indices.Add(indice);
        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.PiecesFinies.Add(piece);
            await db.SaveChangesAsync();
            piece.IndiceCourantId = indice.Id;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await Ecrire(piece.Id, "creation", $"{historique.Sujet} a créé la pièce {piece.Reference} (Client {client.Nom})");
        return Resultat.Ok(piece.Id);
    }

    public async Task<Resultat> ModifierIdentite(int id, SaisieIdentite saisie)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        if (!courant.Peut(Role.Gestion))
        {
            return Resultat.Echec(Resultat.NonAutorise);
        }

        var reference = saisie.Reference?.Trim() ?? string.Empty;
        var designation = saisie.Designation?.Trim() ?? string.Empty;
        if (reference.Length == 0 || designation.Length == 0)
        {
            return Resultat.Echec("La référence et la désignation sont obligatoires.");
        }

        await using var db = await fabrique.CreateDbContextAsync();
        var piece = await db.PiecesFinies.FindAsync(id);
        if (piece is null)
        {
            return Resultat.Echec("Pièce introuvable.");
        }

        if (!await db.Clients.AnyAsync(c => c.Id == saisie.ClientId))
        {
            return Resultat.Echec("Choisissez le client.");
        }

        if (await db.PiecesFinies.AnyAsync(p => p.Id != id && p.ClientId == saisie.ClientId && p.Reference == reference))
        {
            return Resultat.Echec($"La référence {reference} existe déjà pour ce client.");
        }

        (piece.Reference, piece.Designation, piece.ClientId, piece.EmplacementPlanPapier, piece.Remarques) =
            (reference, designation, saisie.ClientId, ServiceReferentiels.Net(saisie.EmplacementPlanPapier), ServiceReferentiels.Net(saisie.Remarques));
        await db.SaveChangesAsync();
        await Ecrire(id, "modification", $"{historique.Sujet} a modifié la pièce {piece.Reference}");
        return Resultat.Ok();
    }

    public async Task<Resultat> Archiver(int id, bool archive)
    {
        if (!courant.Peut(Role.Gestion))
        {
            return Resultat.Echec(Resultat.NonAutorise);
        }

        await using var db = await fabrique.CreateDbContextAsync();
        var piece = await db.PiecesFinies.FindAsync(id);
        if (piece is null)
        {
            return Resultat.Echec("Pièce introuvable.");
        }

        piece.Archive = archive;
        await db.SaveChangesAsync();
        await Ecrire(id, archive ? "archivage" : "desarchivage", $"{historique.Sujet} a {(archive ? "archivé" : "désarchivé")} la pièce {piece.Reference}");
        return Resultat.Ok();
    }

    /// <summary>Suppression impossible si la pièce a des mouvements de stock : il faut l'archiver.</summary>
    public async Task<Resultat> Supprimer(int id)
    {
        if (!courant.Peut(Role.Gestion))
        {
            return Resultat.Echec(Resultat.NonAutorise);
        }

        if (await stock.ADesMouvements(TypeReference.PieceFinie, id))
        {
            return Resultat.Echec("Cette pièce a des mouvements de stock : elle ne peut pas être supprimée. Archivez-la plutôt.");
        }

        await using var db = await fabrique.CreateDbContextAsync();
        var piece = await db.PiecesFinies.FindAsync(id);
        if (piece is null)
        {
            return Resultat.Echec("Pièce introuvable.");
        }

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            await db.Liens.Where(l => l.PieceCibleId == id).ExecuteDeleteAsync();
            db.PiecesFinies.Remove(piece);
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        stockage.SupprimerPiece(id);
        await Ecrire(id, "suppression", $"{historique.Sujet} a supprimé la pièce {piece.Reference}");
        return Resultat.Ok();
    }

    // ---------- Bruts associés ----------

    public async Task<Resultat> AjouterBrut(int pieceId, int brutId, int piecesParBrut = 1)
    {
        var verification = await Charger(pieceId);
        if (verification.Erreur is not null)
        {
            return Resultat.Echec(verification.Erreur);
        }

        await using var db = verification.Db!;
        var piece = verification.Piece!;
        if (piece.Bruts.Any(b => b.BrutId == brutId))
        {
            return Resultat.Echec("Ce brut est déjà associé à la pièce.");
        }

        var brut = await db.Bruts.FindAsync(brutId);
        if (brut is null)
        {
            return Resultat.Echec("Brut introuvable.");
        }

        piece.Bruts.Add(new PieceFinieBrut { BrutId = brutId, PiecesParBrut = Math.Max(1, piecesParBrut), Principal = piece.Bruts.Count == 0 });
        await db.SaveChangesAsync();
        await Ecrire(pieceId, "brut", $"{historique.Sujet} a ajouté le brut {brut.Reference} à la pièce {piece.Reference}");
        return Resultat.Ok();
    }

    /// <summary>Impossible de retirer le dernier brut d'une pièce finie.</summary>
    public async Task<Resultat> RetirerBrut(int pieceId, int brutId)
    {
        var verification = await Charger(pieceId);
        if (verification.Erreur is not null)
        {
            return Resultat.Echec(verification.Erreur);
        }

        await using var db = verification.Db!;
        var piece = verification.Piece!;
        var association = piece.Bruts.FirstOrDefault(b => b.BrutId == brutId);
        if (association is null)
        {
            return Resultat.Echec("Ce brut n'est pas associé à la pièce.");
        }

        if (piece.Bruts.Count == 1)
        {
            return Resultat.Echec("Impossible de retirer le dernier brut : une pièce finie a toujours au moins un brut.");
        }

        piece.Bruts.Remove(association);
        if (association.Principal)
        {
            piece.Bruts[0].Principal = true;
        }

        await db.SaveChangesAsync();
        var reference = await db.Bruts.Where(b => b.Id == brutId).Select(b => b.Reference).FirstOrDefaultAsync();
        await Ecrire(pieceId, "brut", $"{historique.Sujet} a retiré le brut {reference} de la pièce {piece.Reference}");
        return Resultat.Ok();
    }

    public async Task<Resultat> DefinirBrutPrincipal(int pieceId, int brutId)
    {
        var verification = await Charger(pieceId);
        if (verification.Erreur is not null)
        {
            return Resultat.Echec(verification.Erreur);
        }

        await using var db = verification.Db!;
        var piece = verification.Piece!;
        if (piece.Bruts.All(b => b.BrutId != brutId))
        {
            return Resultat.Echec("Ce brut n'est pas associé à la pièce.");
        }

        foreach (var association in piece.Bruts)
        {
            association.Principal = association.BrutId == brutId;
        }

        await db.SaveChangesAsync();
        var reference = await db.Bruts.Where(b => b.Id == brutId).Select(b => b.Reference).FirstOrDefaultAsync();
        await Ecrire(pieceId, "brut", $"{historique.Sujet} a choisi {reference} comme brut principal de la pièce {piece.Reference}");
        return Resultat.Ok();
    }

    public async Task<Resultat> ModifierPiecesParBrut(int pieceId, int brutId, int piecesParBrut)
    {
        if (piecesParBrut < 1)
        {
            return Resultat.Echec("Le nombre de pièces obtenues par brut doit être au moins 1.");
        }

        var verification = await Charger(pieceId);
        if (verification.Erreur is not null)
        {
            return Resultat.Echec(verification.Erreur);
        }

        await using var db = verification.Db!;
        var association = verification.Piece!.Bruts.FirstOrDefault(b => b.BrutId == brutId);
        if (association is null)
        {
            return Resultat.Echec("Ce brut n'est pas associé à la pièce.");
        }

        association.PiecesParBrut = piecesParBrut;
        await db.SaveChangesAsync();
        return Resultat.Ok();
    }

    // ---------- Indices et spécifications ----------

    /// <summary>Nouvel indice : la description du changement est obligatoire ; spécifications et gamme recopiées sur demande.</summary>
    public async Task<Resultat<int>> NouvelIndice(int pieceId, string code, string description, bool recopier)
    {
        if (!courant.Peut(Role.Gestion))
        {
            return Resultat.Echec<int>(Resultat.NonAutorise);
        }

        var nouveauCode = code?.Trim() ?? string.Empty;
        if (nouveauCode.Length == 0)
        {
            return Resultat.Echec<int>("Indiquez le nouvel indice.");
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            return Resultat.Echec<int>("Indiquez en quelques mots ce qui a changé.");
        }

        await using var db = await fabrique.CreateDbContextAsync();
        var piece = await db.PiecesFinies.Include(p => p.Indices).FirstOrDefaultAsync(p => p.Id == pieceId);
        if (piece is null)
        {
            return Resultat.Echec<int>("Pièce introuvable.");
        }

        if (piece.Indices.Any(i => string.Equals(i.Code, nouveauCode, StringComparison.OrdinalIgnoreCase)))
        {
            return Resultat.Echec<int>($"L'indice {nouveauCode} existe déjà pour cette pièce.");
        }

        var precedent = await db.Indices
            .Include(i => i.Specification)
            .Include(i => i.LignesSpecification)
            .Include(i => i.Operations)
            .AsSplitQuery()
            .FirstOrDefaultAsync(i => i.Id == piece.IndiceCourantId);
        var indice = new Indice
        {
            PieceFinieId = pieceId,
            Code = nouveauCode,
            Date = Maintenant(),
            AuteurId = Auteur(),
            DescriptionChangement = description.Trim(),
        };
        if (recopier && precedent is not null)
        {
            if (precedent.Specification is { } s)
            {
                indice.Specification = new Specification
                {
                    TraitementThermique = s.TraitementThermique,
                    TraitementSurface = s.TraitementSurface,
                    ToleranceGenerale = s.ToleranceGenerale,
                    Masse = s.Masse,
                    Remarques = s.Remarques,
                };
            }

            indice.LignesSpecification = precedent.LignesSpecification
                .Select(l => new SpecificationLigne { Libelle = l.Libelle, Valeur = l.Valeur, Ordre = l.Ordre }).ToList();
            indice.Operations = precedent.Operations
                .Select(o => new OperationGamme { Numero = o.Numero, PosteId = o.PosteId, Description = o.Description, TempsEstimeMin = o.TempsEstimeMin, Remarque = o.Remarque, Ordre = o.Ordre })
                .ToList();
        }

        await using (var transaction = await db.Database.BeginTransactionAsync())
        {
            db.Indices.Add(indice);
            await db.SaveChangesAsync();
            piece.IndiceCourantId = indice.Id;
            await db.SaveChangesAsync();
            await transaction.CommitAsync();
        }

        await Ecrire(pieceId, "indice", $"{historique.Sujet} a passé l'indice de {precedent?.Code ?? "?"} à {indice.Code} (pièce {piece.Reference}) : {indice.DescriptionChangement}");
        return Resultat.Ok(indice.Id);
    }

    public async Task<Resultat> EnregistrerSpecification(int indiceId, SaisieSpecification saisie)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        var verification = await ChargerIndiceCourant(indiceId);
        if (verification.Erreur is not null)
        {
            return Resultat.Echec(verification.Erreur);
        }

        await using var db = verification.Db!;
        var indice = await db.Indices.Include(i => i.Specification).Include(i => i.LignesSpecification).FirstAsync(i => i.Id == indiceId);
        AppliquerSpecification(indice, saisie);
        await db.SaveChangesAsync();
        await Ecrire(indice.PieceFinieId, "specification", $"{historique.Sujet} a modifié les spécifications (indice {indice.Code}) de la pièce {verification.Reference}");
        return Resultat.Ok();
    }

    private static void AppliquerSpecification(Indice indice, SaisieSpecification s)
    {
        indice.Specification ??= new Specification();
        indice.Specification.TraitementThermique = ServiceReferentiels.Net(s.TraitementThermique);
        indice.Specification.TraitementSurface = ServiceReferentiels.Net(s.TraitementSurface);
        indice.Specification.ToleranceGenerale = ServiceReferentiels.Net(s.ToleranceGenerale);
        indice.Specification.Masse = ServiceReferentiels.Net(s.Masse);
        indice.Specification.Remarques = ServiceReferentiels.Net(s.Remarques);
        indice.LignesSpecification.Clear();
        var ordre = 0;
        foreach (var ligne in s.Lignes.Where(l => !string.IsNullOrWhiteSpace(l.Libelle) || !string.IsNullOrWhiteSpace(l.Valeur)))
        {
            indice.LignesSpecification.Add(new SpecificationLigne { Libelle = ligne.Libelle.Trim(), Valeur = ligne.Valeur.Trim(), Ordre = ordre++ });
        }
    }

    // ---------- Gamme (indice courant uniquement) ----------

    /// <summary>N° proposé pour la prochaine opération : 10, 20, 30…</summary>
    public static int ProchainNumero(IEnumerable<OperationAffichee> operations) =>
        ((operations.Select(o => o.Numero).DefaultIfEmpty(0).Max() / 10) + 1) * 10;

    public async Task<Resultat> EnregistrerOperation(int indiceId, int? operationId, SaisieOperation saisie)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        if (string.IsNullOrWhiteSpace(saisie.Description))
        {
            return Resultat.Echec("Décrivez l'opération.");
        }

        if (saisie.TempsEstimeMin is < 0)
        {
            return Resultat.Echec("Le temps estimé ne peut pas être négatif.");
        }

        var verification = await ChargerIndiceCourant(indiceId);
        if (verification.Erreur is not null)
        {
            return Resultat.Echec(verification.Erreur);
        }

        await using var db = verification.Db!;
        var operations = await db.Operations.Where(o => o.IndiceId == indiceId).ToListAsync();
        OperationGamme? operation;
        if (operationId is null)
        {
            operation = new OperationGamme
            {
                IndiceId = indiceId,
                Ordre = operations.Select(o => o.Ordre).DefaultIfEmpty(0).Max() + 1,
                Numero = saisie.Numero ?? (((operations.Select(o => o.Numero).DefaultIfEmpty(0).Max() / 10) + 1) * 10),
            };
            db.Operations.Add(operation);
        }
        else
        {
            operation = operations.FirstOrDefault(o => o.Id == operationId);
            if (operation is null)
            {
                return Resultat.Echec("Opération introuvable.");
            }

            operation.Numero = saisie.Numero ?? operation.Numero;
        }

        operation.PosteId = saisie.PosteId;
        operation.Description = saisie.Description.Trim();
        operation.TempsEstimeMin = saisie.TempsEstimeMin;
        operation.Remarque = ServiceReferentiels.Net(saisie.Remarque);
        await db.SaveChangesAsync();
        await Ecrire(verification.PieceId, "gamme", $"{historique.Sujet} a {(operationId is null ? "ajouté" : "modifié")} l'opération {operation.Numero} de la gamme (pièce {verification.Reference})");
        return Resultat.Ok();
    }

    public async Task<Resultat> SupprimerOperation(int indiceId, int operationId)
    {
        var verification = await ChargerIndiceCourant(indiceId);
        if (verification.Erreur is not null)
        {
            return Resultat.Echec(verification.Erreur);
        }

        await using var db = verification.Db!;
        var operation = await db.Operations.FirstOrDefaultAsync(o => o.Id == operationId && o.IndiceId == indiceId);
        if (operation is null)
        {
            return Resultat.Echec("Opération introuvable.");
        }

        db.Operations.Remove(operation);
        await db.SaveChangesAsync();
        await Ecrire(verification.PieceId, "gamme", $"{historique.Sujet} a supprimé l'opération {operation.Numero} de la gamme (pièce {verification.Reference})");
        return Resultat.Ok();
    }

    /// <summary>Monte (−1) ou descend (+1) une opération ; les numéros restent croissants.</summary>
    public async Task<Resultat> DeplacerOperation(int indiceId, int operationId, int sens)
    {
        var verification = await ChargerIndiceCourant(indiceId);
        if (verification.Erreur is not null)
        {
            return Resultat.Echec(verification.Erreur);
        }

        await using var db = verification.Db!;
        var operations = await db.Operations.Where(o => o.IndiceId == indiceId).OrderBy(o => o.Ordre).ThenBy(o => o.Numero).ToListAsync();
        var position = operations.FindIndex(o => o.Id == operationId);
        var cible = position + Math.Sign(sens);
        if (position < 0 || cible < 0 || cible >= operations.Count)
        {
            return Resultat.Ok();
        }

        var (a, b) = (operations[position], operations[cible]);
        (a.Numero, b.Numero) = (b.Numero, a.Numero);
        operations[position] = b;
        operations[cible] = a;
        for (var i = 0; i < operations.Count; i++)
        {
            operations[i].Ordre = i + 1;
        }

        await db.SaveChangesAsync();
        return Resultat.Ok();
    }

    // ---------- Plans ----------

    public async Task<Resultat> AjouterPlan(int indiceId, string nomFichier, Stream flux, long taille)
    {
        ArgumentNullException.ThrowIfNull(flux);
        if (!courant.Peut(Role.Gestion))
        {
            return Resultat.Echec(Resultat.NonAutorise);
        }

        var type = StockagePlans.TypeAccepte(nomFichier);
        if (type is null)
        {
            return Resultat.Echec($"« {nomFichier} » : seuls les fichiers PDF, PNG et JPG sont acceptés.");
        }

        if (taille > stockage.TailleMax)
        {
            return Resultat.Echec($"« {nomFichier} » dépasse la taille maximale ({stockage.TailleMaxMo} Mo).");
        }

        await using var db = await fabrique.CreateDbContextAsync();
        var indice = await db.Indices.FirstOrDefaultAsync(i => i.Id == indiceId);
        if (indice is null)
        {
            return Resultat.Echec("Indice introuvable.");
        }

        var (chemin, tailleEcrite) = await stockage.Enregistrer(indice.PieceFinieId, indice.Code, nomFichier, flux);
        db.Plans.Add(new Plan
        {
            IndiceId = indiceId,
            NomFichier = Path.GetFileName(chemin),
            Chemin = chemin,
            TypeMime = type,
            Taille = tailleEcrite,
            DateAjout = Maintenant(),
            AuteurId = Auteur(),
        });
        await db.SaveChangesAsync();
        var reference = await db.PiecesFinies.Where(p => p.Id == indice.PieceFinieId).Select(p => p.Reference).FirstAsync();
        await Ecrire(indice.PieceFinieId, "plan", $"{historique.Sujet} a ajouté le plan {Path.GetFileName(chemin)} (indice {indice.Code}) à la pièce {reference}");
        return Resultat.Ok();
    }

    public async Task<Resultat> SupprimerPlan(int planId)
    {
        if (!courant.Peut(Role.Gestion))
        {
            return Resultat.Echec(Resultat.NonAutorise);
        }

        await using var db = await fabrique.CreateDbContextAsync();
        var plan = await db.Plans.FirstOrDefaultAsync(p => p.Id == planId);
        if (plan is null)
        {
            return Resultat.Echec("Plan introuvable.");
        }

        var indice = await db.Indices.FirstAsync(i => i.Id == plan.IndiceId);
        db.Plans.Remove(plan);
        await db.SaveChangesAsync();
        stockage.Supprimer(plan.Chemin);
        var reference = await db.PiecesFinies.Where(p => p.Id == indice.PieceFinieId).Select(p => p.Reference).FirstAsync();
        await Ecrire(indice.PieceFinieId, "plan", $"{historique.Sujet} a supprimé le plan {plan.NomFichier} (indice {indice.Code}) de la pièce {reference}");
        return Resultat.Ok();
    }

    // ---------- Liens ----------

    public async Task<Resultat> AjouterLien(int pieceId, TypeLien type, int? cibleId, string? url, string? libelle)
    {
        var verification = await Charger(pieceId);
        if (verification.Erreur is not null)
        {
            return Resultat.Echec(verification.Erreur);
        }

        await using var db = verification.Db!;
        var piece = verification.Piece!;
        string phrase;
        if (type == TypeLien.Externe)
        {
            var adresse = url?.Trim() ?? string.Empty;
            if (!Uri.TryCreate(adresse, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return Resultat.Echec("Indiquez une adresse complète, commençant par http:// ou https://.");
            }

            db.Liens.Add(new Lien { PieceFinieId = pieceId, Type = type, Url = adresse, Libelle = ServiceReferentiels.Net(libelle) ?? adresse });
            phrase = $"{historique.Sujet} a ajouté le lien externe « {ServiceReferentiels.Net(libelle) ?? adresse} » à la pièce {piece.Reference}";
        }
        else
        {
            if (cibleId is null || cibleId == pieceId)
            {
                return Resultat.Echec("Choisissez une autre pièce finie.");
            }

            var cible = await db.PiecesFinies.FindAsync(cibleId.Value);
            if (cible is null)
            {
                return Resultat.Echec("Pièce introuvable.");
            }

            var existe = await db.Liens.AnyAsync(l => l.Type != TypeLien.Externe
                && ((l.PieceFinieId == pieceId && l.PieceCibleId == cibleId) || (l.PieceFinieId == cibleId && l.PieceCibleId == pieceId)));
            if (existe)
            {
                return Resultat.Echec($"Un lien existe déjà avec la pièce {cible.Reference}.");
            }

            db.Liens.Add(new Lien { PieceFinieId = pieceId, Type = type, PieceCibleId = cibleId });
            if (type is TypeLien.Remplace or TypeLien.RemplaceePar)
            {
                // Lien réciproque créé automatiquement.
                db.Liens.Add(new Lien { PieceFinieId = cible.Id, Type = LibellesLien.Reciproque(type), PieceCibleId = pieceId });
            }

            phrase = $"{historique.Sujet} a lié la pièce {piece.Reference} à {cible.Reference} ({LibellesLien.Libelle(type).ToLowerInvariant()})";
        }

        await db.SaveChangesAsync();
        await Ecrire(pieceId, "lien", phrase);
        return Resultat.Ok();
    }

    public async Task<Resultat> SupprimerLien(int pieceId, int lienId)
    {
        if (!courant.Peut(Role.Gestion))
        {
            return Resultat.Echec(Resultat.NonAutorise);
        }

        await using var db = await fabrique.CreateDbContextAsync();
        var lien = await db.Liens.FirstOrDefaultAsync(l => l.Id == lienId && (l.PieceFinieId == pieceId || l.PieceCibleId == pieceId));
        if (lien is null)
        {
            return Resultat.Echec("Lien introuvable.");
        }

        db.Liens.Remove(lien);
        if (lien.Type is TypeLien.Remplace or TypeLien.RemplaceePar)
        {
            var reciproque = LibellesLien.Reciproque(lien.Type);
            db.Liens.RemoveRange(db.Liens.Where(l => l.PieceFinieId == lien.PieceCibleId && l.PieceCibleId == lien.PieceFinieId && l.Type == reciproque));
        }

        await db.SaveChangesAsync();
        var reference = await db.PiecesFinies.Where(p => p.Id == pieceId).Select(p => p.Reference).FirstOrDefaultAsync();
        await Ecrire(pieceId, "lien", $"{historique.Sujet} a supprimé un lien de la pièce {reference}");
        return Resultat.Ok();
    }

    // ---------- Outils ----------

    private DateTime Maintenant() => horloge.GetLocalNow().DateTime;

    private int? Auteur() => courant.EstConnecte ? courant.Id : null;

    private Task Ecrire(int pieceId, string action, string phrase) => historique.Ecrire(Module, Type, pieceId, action, phrase);

    private sealed record Chargement(AnnuaireDbContext? Db, PieceFinie? Piece, string? Erreur);

    /// <summary>Contexte et pièce (avec ses bruts), après contrôle du rôle Gestion. Le contexte est à libérer par l'appelant.</summary>
    private async Task<Chargement> Charger(int pieceId)
    {
        if (!courant.Peut(Role.Gestion))
        {
            return new(null, null, Resultat.NonAutorise);
        }

        var db = await fabrique.CreateDbContextAsync();
        var piece = await db.PiecesFinies.Include(p => p.Bruts).FirstOrDefaultAsync(p => p.Id == pieceId);
        if (piece is null)
        {
            await db.DisposeAsync();
            return new(null, null, "Pièce introuvable.");
        }

        piece.Bruts = piece.Bruts.OrderByDescending(b => b.Principal).ThenBy(b => b.BrutId).ToList();
        return new(db, piece, null);
    }

    private sealed record ChargementIndice(AnnuaireDbContext? Db, int PieceId, string Reference, string? Erreur);

    /// <summary>Les indices précédents restent en lecture seule : seules les données de l'indice courant se modifient.</summary>
    private async Task<ChargementIndice> ChargerIndiceCourant(int indiceId)
    {
        if (!courant.Peut(Role.Gestion))
        {
            return new(null, 0, string.Empty, Resultat.NonAutorise);
        }

        var db = await fabrique.CreateDbContextAsync();
        var indice = await db.Indices.AsNoTracking().FirstOrDefaultAsync(i => i.Id == indiceId);
        var piece = indice is null ? null : await db.PiecesFinies.AsNoTracking().FirstOrDefaultAsync(p => p.Id == indice.PieceFinieId);
        if (indice is null || piece is null)
        {
            await db.DisposeAsync();
            return new(null, 0, string.Empty, "Indice introuvable.");
        }

        if (piece.IndiceCourantId != indiceId)
        {
            await db.DisposeAsync();
            return new(null, 0, string.Empty, "Les indices précédents ne se modifient pas : passez par l'indice courant.");
        }

        return new(db, piece.Id, piece.Reference, null);
    }
}
