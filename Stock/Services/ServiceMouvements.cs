using Barthazon.Commun.Composants;
using Barthazon.Commun.Contrats;
using Barthazon.Commun.Outils;
using Barthazon.Commun.Securite;
using Barthazon.Stock.Donnees;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Stock.Services;

public sealed record DemandeArrivage(int BrutId, decimal Quantite, int? FournisseurId, DateTime? Date, string? NumeroBL, string? Commentaire);

public sealed record DemandeFabrication(int PieceFinieId, int? BrutId, decimal BrutsConsommes, decimal PiecesObtenues, bool NePasDeduireBrut, DateTime? Date, string? Commentaire);

public sealed record DemandeSortie(TypeReference Type, int ReferenceId, decimal Quantite, MotifSortie Motif, int? ClientId, DateTime? Date, string? NumeroBL, string? Commentaire);

public sealed record DemandeAjustement(TypeReference Type, int ReferenceId, decimal QuantiteComptee, string? Commentaire);

/// <summary>Résultat d'une saisie : message court (« Arrivage enregistré : +20 barres ») et quantités après.</summary>
public sealed record ResultatMouvement(string Message, IReadOnlyList<int> Mouvements);

/// <summary>Écart trouvé par « Vérifier les stocks ».</summary>
public sealed record EcartStock(TypeReference Type, int ReferenceId, string Reference, decimal Enregistre, decimal Calcule);

/// <summary>
/// Saisie des mouvements de stock. Chaque changement de quantité est un mouvement ; le niveau est mis à jour
/// dans la même transaction. Les mouvements ne sont jamais supprimés, seulement annulés.
/// </summary>
public sealed class ServiceMouvements(
    IDbContextFactory<StockDbContext> fabrique,
    IAnnuaireLecture annuaire,
    IUtilisateurCourant courant,
    SignauxApplication signaux,
    TimeProvider horloge)
{
    public const string Signal = "stock";

    /// <summary>Un seul enregistrement à la fois (un seul serveur) : le calcul « quantité après » reste exact.</summary>
    private static readonly SemaphoreSlim Verrou = new(1, 1);

    public async Task<Resultat<ResultatMouvement>> EnregistrerArrivage(DemandeArrivage demande)
    {
        ArgumentNullException.ThrowIfNull(demande);
        var erreur = Controler(Role.Atelier, demande.Quantite, demande.Date);
        if (erreur is not null)
        {
            return Resultat.Echec<ResultatMouvement>(erreur);
        }

        var brut = await annuaire.ObtenirBrut(demande.BrutId);
        if (brut is null)
        {
            return Resultat.Echec<ResultatMouvement>("Brut introuvable.");
        }

        var ids = await Ecrire(
        [
            new Mouvement
            {
                TypeReference = TypeReference.Brut,
                ReferenceId = brut.Id,
                TypeMouvement = TypeMouvement.Arrivage,
                Quantite = demande.Quantite,
                FournisseurId = demande.FournisseurId,
                NumeroBL = Net(demande.NumeroBL),
                Commentaire = Net(demande.Commentaire),
            },
        ], demande.Date);
        return Resultat.Ok(new ResultatMouvement($"Arrivage enregistré : {Format.QuantiteSignee(demande.Quantite, brut.Unite)} de {brut.Reference}", ids));
    }

    /// <summary>Fabrication : − bruts consommés sur le brut, + pièces obtenues sur la pièce finie (même opération).</summary>
    public async Task<Resultat<ResultatMouvement>> EnregistrerFabrication(DemandeFabrication demande)
    {
        ArgumentNullException.ThrowIfNull(demande);
        var erreur = Controler(Role.Atelier, demande.PiecesObtenues, demande.Date);
        if (erreur is not null)
        {
            return Resultat.Echec<ResultatMouvement>(erreur);
        }

        var piece = await annuaire.ObtenirPieceFinie(demande.PieceFinieId);
        if (piece is null)
        {
            return Resultat.Echec<ResultatMouvement>("Pièce finie introuvable.");
        }

        var mouvements = new List<Mouvement>();
        var message = $"Fabrication enregistrée : {Format.QuantiteSignee(demande.PiecesObtenues, Unite.Piece)} {piece.Reference}";
        if (!demande.NePasDeduireBrut)
        {
            if (demande.BrutsConsommes <= 0)
            {
                return Resultat.Echec<ResultatMouvement>("Indiquez le nombre de bruts consommés (ou cochez « Ne pas déduire de brut »).");
            }

            var associe = (await annuaire.BrutsDeLaPiece(piece.Id)).FirstOrDefault(b => b.Brut.Id == demande.BrutId);
            if (associe is null)
            {
                return Resultat.Echec<ResultatMouvement>("Choisissez un des bruts de la pièce.");
            }

            mouvements.Add(new Mouvement
            {
                TypeReference = TypeReference.Brut,
                ReferenceId = associe.Brut.Id,
                TypeMouvement = TypeMouvement.Fabrication,
                Quantite = -demande.BrutsConsommes,
                Commentaire = Net(demande.Commentaire),
            });
            message += $" ({Format.QuantiteSignee(-demande.BrutsConsommes, associe.Brut.Unite)} {associe.Brut.Reference})";
        }

        mouvements.Add(new Mouvement
        {
            TypeReference = TypeReference.PieceFinie,
            ReferenceId = piece.Id,
            TypeMouvement = TypeMouvement.Fabrication,
            Quantite = demande.PiecesObtenues,
            Commentaire = Net(demande.Commentaire),
        });
        var ids = await Ecrire(mouvements, demande.Date);
        return Resultat.Ok(new ResultatMouvement(message, ids));
    }

    public async Task<Resultat<ResultatMouvement>> EnregistrerSortie(DemandeSortie demande)
    {
        ArgumentNullException.ThrowIfNull(demande);
        var erreur = Controler(Role.Atelier, demande.Quantite, demande.Date);
        if (erreur is not null)
        {
            return Resultat.Echec<ResultatMouvement>(erreur);
        }

        if (!ReglesStock.Motifs(demande.Type).Contains(demande.Motif))
        {
            return Resultat.Echec<ResultatMouvement>("Motif de sortie invalide.");
        }

        var (reference, unite) = await Decrire(demande.Type, demande.ReferenceId);
        if (reference is null)
        {
            return Resultat.Echec<ResultatMouvement>("Référence introuvable.");
        }

        var ids = await Ecrire(
        [
            new Mouvement
            {
                TypeReference = demande.Type,
                ReferenceId = demande.ReferenceId,
                TypeMouvement = TypeMouvement.Sortie,
                Motif = demande.Motif,
                Quantite = -demande.Quantite,
                ClientId = demande.Motif == MotifSortie.LivraisonClient ? demande.ClientId : null,
                NumeroBL = Net(demande.NumeroBL),
                Commentaire = Net(demande.Commentaire),
            },
        ], demande.Date);
        return Resultat.Ok(new ResultatMouvement($"Sortie enregistrée : {Format.QuantiteSignee(-demande.Quantite, unite)} {reference} ({ReglesStock.Libelle(demande.Motif).ToLowerInvariant()})", ids));
    }

    /// <summary>Inventaire : on saisit la quantité comptée, le mouvement d'ajustement est égal à l'écart.</summary>
    public async Task<Resultat<ResultatMouvement>> EnregistrerAjustement(DemandeAjustement demande)
    {
        ArgumentNullException.ThrowIfNull(demande);
        if (!courant.Peut(Role.Gestion))
        {
            return Resultat.Echec<ResultatMouvement>(Resultat.NonAutorise);
        }

        if (demande.QuantiteComptee < 0)
        {
            return Resultat.Echec<ResultatMouvement>("La quantité comptée ne peut pas être négative.");
        }

        var (reference, unite) = await Decrire(demande.Type, demande.ReferenceId);
        if (reference is null)
        {
            return Resultat.Echec<ResultatMouvement>("Référence introuvable.");
        }

        await Verrou.WaitAsync();
        try
        {
            await using var db = await fabrique.CreateDbContextAsync();
            var actuel = (await db.Niveaux.AsNoTracking().FirstOrDefaultAsync(n => n.TypeReference == demande.Type && n.ReferenceId == demande.ReferenceId))?.Quantite ?? 0;
            var ecart = demande.QuantiteComptee - actuel;
            if (ecart == 0)
            {
                return Resultat.Echec<ResultatMouvement>($"Le stock est déjà de {Format.Quantite(actuel, unite)} : rien à corriger.");
            }

            var ids = await EcrireSansVerrou(db,
            [
                new Mouvement
                {
                    TypeReference = demande.Type,
                    ReferenceId = demande.ReferenceId,
                    TypeMouvement = TypeMouvement.Ajustement,
                    Quantite = ecart,
                    Commentaire = Net(demande.Commentaire),
                },
            ], null);
            signaux.Emettre(Signal);
            return Resultat.Ok(new ResultatMouvement($"Inventaire enregistré : {Format.Quantite(demande.QuantiteComptee, unite)} {reference} (écart {Format.QuantiteSignee(ecart, unite)})", ids));
        }
        finally
        {
            Verrou.Release();
        }
    }

    /// <summary>Vrai si l'utilisateur courant peut annuler ce mouvement (auteur le jour même, ou Gestion).</summary>
    public bool PeutAnnuler(Mouvement mouvement)
    {
        ArgumentNullException.ThrowIfNull(mouvement);
        if (mouvement.TypeMouvement == TypeMouvement.Annulation || mouvement.AnnuleParId is not null)
        {
            return false;
        }

        return courant.Peut(Role.Gestion)
            || (courant.Peut(Role.Atelier) && mouvement.UtilisateurId == courant.Id && mouvement.DateSaisie.Date == horloge.GetLocalNow().Date);
    }

    /// <summary>Annule un mouvement par des mouvements inverses ; une fabrication s'annule en entier.</summary>
    public async Task<Resultat<ResultatMouvement>> Annuler(int mouvementId)
    {
        await Verrou.WaitAsync();
        try
        {
            await using var db = await fabrique.CreateDbContextAsync();
            var mouvement = await db.Mouvements.AsNoTracking().FirstOrDefaultAsync(m => m.Id == mouvementId);
            if (mouvement is null)
            {
                return Resultat.Echec<ResultatMouvement>("Mouvement introuvable.");
            }

            if (mouvement.TypeMouvement == TypeMouvement.Annulation)
            {
                return Resultat.Echec<ResultatMouvement>("Une annulation ne s'annule pas : saisissez un nouveau mouvement.");
            }

            if (mouvement.AnnuleParId is not null)
            {
                return Resultat.Echec<ResultatMouvement>("Ce mouvement est déjà annulé.");
            }

            if (!PeutAnnuler(mouvement))
            {
                return Resultat.Echec<ResultatMouvement>("Seul son auteur (le jour même) ou la Gestion peut annuler ce mouvement.");
            }

            var operation = await db.Mouvements
                .Where(m => m.OperationId == mouvement.OperationId && m.TypeMouvement != TypeMouvement.Annulation && m.AnnuleParId == null)
                .OrderBy(m => m.Id)
                .ToListAsync();
            var inverses = operation.Select(m => new Mouvement
            {
                TypeReference = m.TypeReference,
                ReferenceId = m.ReferenceId,
                TypeMouvement = TypeMouvement.Annulation,
                Quantite = -m.Quantite,
                AnnuleId = m.Id,
                Commentaire = $"Annulation : {ReglesStock.Libelle(m.TypeMouvement).ToLowerInvariant()} du {Format.DateHeure(m.Date)}",
            }).ToList();
            var ids = await EcrireSansVerrou(db, inverses, null, apres: () =>
            {
                for (var i = 0; i < operation.Count; i++)
                {
                    operation[i].AnnuleParId = inverses[i].Id;
                }
            });
            signaux.Emettre(Signal);
            var feminin = mouvement.TypeMouvement is TypeMouvement.Fabrication or TypeMouvement.Sortie;
            var libelle = $"{ReglesStock.Libelle(mouvement.TypeMouvement)} annulé{(feminin ? "e" : null)}";
            return Resultat.Ok(new ResultatMouvement(operation.Count > 1 ? $"{libelle} en entier ({operation.Count} mouvements)" : libelle, ids));
        }
        finally
        {
            Verrou.Release();
        }
    }

    /// <summary>Seuil d'alerte (Gestion) ; null pour le retirer.</summary>
    public async Task<Resultat> ModifierSeuil(TypeReference type, int referenceId, decimal? seuil)
    {
        if (!courant.Peut(Role.Gestion))
        {
            return Resultat.Echec(Resultat.NonAutorise);
        }

        if (seuil is < 0)
        {
            return Resultat.Echec("Le seuil ne peut pas être négatif.");
        }

        await using var db = await fabrique.CreateDbContextAsync();
        var niveau = await db.Niveaux.FirstOrDefaultAsync(n => n.TypeReference == type && n.ReferenceId == referenceId);
        if (niveau is null)
        {
            niveau = new NiveauStock { TypeReference = type, ReferenceId = referenceId };
            db.Niveaux.Add(niveau);
        }

        niveau.SeuilAlerte = seuil;
        await db.SaveChangesAsync();
        signaux.Emettre(Signal);
        return Resultat.Ok();
    }

    /// <summary>« Vérifier les stocks » : recalcule chaque quantité à partir des mouvements.</summary>
    public async Task<IReadOnlyList<EcartStock>> Verifier()
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var sommes = (await db.Mouvements.AsNoTracking().Select(m => new { m.TypeReference, m.ReferenceId, m.Quantite }).ToListAsync())
            .GroupBy(m => (m.TypeReference, m.ReferenceId))
            .ToDictionary(g => g.Key, g => g.Sum(m => m.Quantite));
        var niveaux = await db.Niveaux.AsNoTracking().ToListAsync();
        var cles = sommes.Keys.Union(niveaux.Select(n => (n.TypeReference, n.ReferenceId)));
        var ecarts = new List<EcartStock>();
        foreach (var (type, id) in cles)
        {
            var enregistre = niveaux.FirstOrDefault(n => n.TypeReference == type && n.ReferenceId == id)?.Quantite ?? 0;
            var calcule = sommes.GetValueOrDefault((type, id));
            if (enregistre != calcule)
            {
                var (reference, _) = await Decrire(type, id);
                ecarts.Add(new EcartStock(type, id, reference ?? $"#{id}", enregistre, calcule));
            }
        }

        return ecarts;
    }

    /// <summary>Corrige les niveaux d'après les mouvements (Administrateur).</summary>
    public async Task<Resultat<int>> Corriger()
    {
        if (!courant.Peut(Role.Administrateur))
        {
            return Resultat.Echec<int>(Resultat.NonAutorise);
        }

        var ecarts = await Verifier();
        await using var db = await fabrique.CreateDbContextAsync();
        foreach (var ecart in ecarts)
        {
            var niveau = await db.Niveaux.FirstOrDefaultAsync(n => n.TypeReference == ecart.Type && n.ReferenceId == ecart.ReferenceId);
            if (niveau is null)
            {
                niveau = new NiveauStock { TypeReference = ecart.Type, ReferenceId = ecart.ReferenceId, DernierMouvement = Maintenant() };
                db.Niveaux.Add(niveau);
            }

            niveau.Quantite = ecart.Calcule;
        }

        await db.SaveChangesAsync();
        signaux.Emettre(Signal);
        return Resultat.Ok(ecarts.Count);
    }

    private string? Controler(Role role, decimal quantite, DateTime? date)
    {
        if (!courant.Peut(role))
        {
            return Resultat.NonAutorise;
        }

        if (quantite <= 0)
        {
            return "La quantité doit être supérieure à zéro.";
        }

        return date is { } d && d.Date > horloge.GetLocalNow().Date ? "La date ne peut pas être dans le futur." : null;
    }

    private async Task<(string? Reference, Unite Unite)> Decrire(TypeReference type, int id)
    {
        if (type == TypeReference.Brut)
        {
            var brut = await annuaire.ObtenirBrut(id);
            return (brut?.Reference, brut?.Unite ?? Unite.Piece);
        }

        return ((await annuaire.ObtenirPieceFinie(id))?.Reference, Unite.Piece);
    }

    private async Task<IReadOnlyList<int>> Ecrire(List<Mouvement> mouvements, DateTime? date)
    {
        await Verrou.WaitAsync();
        try
        {
            await using var db = await fabrique.CreateDbContextAsync();
            var ids = await EcrireSansVerrou(db, mouvements, date);
            signaux.Emettre(Signal);
            return ids;
        }
        finally
        {
            Verrou.Release();
        }
    }

    /// <summary>Insère les mouvements et met à jour les niveaux dans une seule transaction.</summary>
    private async Task<IReadOnlyList<int>> EcrireSansVerrou(StockDbContext db, List<Mouvement> mouvements, DateTime? date, Action? apres = null)
    {
        var maintenant = Maintenant();
        var operation = Guid.NewGuid();
        var dateMouvement = date is { } d && d.Date != maintenant.Date ? d.Date + maintenant.TimeOfDay : maintenant;

        await using var transaction = await db.Database.BeginTransactionAsync();
        foreach (var mouvement in mouvements)
        {
            var niveau = db.Niveaux.Local.FirstOrDefault(n => n.TypeReference == mouvement.TypeReference && n.ReferenceId == mouvement.ReferenceId)
                ?? await db.Niveaux.FirstOrDefaultAsync(n => n.TypeReference == mouvement.TypeReference && n.ReferenceId == mouvement.ReferenceId);
            if (niveau is null)
            {
                niveau = new NiveauStock { TypeReference = mouvement.TypeReference, ReferenceId = mouvement.ReferenceId };
                db.Niveaux.Add(niveau);
            }

            niveau.Quantite += mouvement.Quantite;
            niveau.DernierMouvement = maintenant;
            mouvement.OperationId = operation;
            mouvement.Date = dateMouvement;
            mouvement.DateSaisie = maintenant;
            mouvement.UtilisateurId = courant.Id;
            mouvement.QuantiteApres = niveau.Quantite;
            db.Mouvements.Add(mouvement);
        }

        await db.SaveChangesAsync();
        if (apres is not null)
        {
            apres();
            await db.SaveChangesAsync();
        }

        await transaction.CommitAsync();
        return mouvements.Select(m => m.Id).ToList();
    }

    private DateTime Maintenant() => horloge.GetLocalNow().DateTime;

    private static string? Net(string? texte) => string.IsNullOrWhiteSpace(texte) ? null : texte.Trim();
}
