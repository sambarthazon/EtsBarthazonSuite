using Barthazon.Commun.Donnees;
using Barthazon.Commun.Securite;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Commun.Historique;

/// <summary>Ligne d'historique prête à afficher.</summary>
public sealed record LigneHistorique(DateTime Date, string? Utilisateur, string Action, string Phrase);

/// <summary>
/// Journal des modifications (table com_Historique), alimenté par les services de chaque application
/// avec une phrase lisible : « Marc a créé la pièce 4512-B (Client Dupont) ».
/// </summary>
public sealed class ServiceHistorique(IDbContextFactory<CommunDbContext> fabrique, IUtilisateurCourant courant, TimeProvider horloge)
{
    /// <summary>Inscrit une ligne. La phrase commence par le prénom de l'utilisateur courant si elle est fournie sans sujet.</summary>
    public async Task Ecrire(string module, string typeObjet, int objetId, string action, string phrase)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        db.Historique.Add(new EntreeHistorique
        {
            Date = horloge.GetLocalNow().DateTime,
            UtilisateurId = courant.EstConnecte ? courant.Id : null,
            Module = module,
            TypeObjet = typeObjet,
            ObjetId = objetId,
            Action = action,
            Phrase = phrase.Length > 500 ? phrase[..500] : phrase,
        });
        await db.SaveChangesAsync();
    }

    /// <summary>Prénom de l'utilisateur courant, pour commencer une phrase (« Marc a … »).</summary>
    public string Sujet => courant.EstConnecte ? courant.Prenom : "Quelqu'un";

    public async Task<IReadOnlyList<LigneHistorique>> Lister(string typeObjet, int objetId, int maximum = 200)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var lignes = await db.Historique.AsNoTracking()
            .Where(h => h.TypeObjet == typeObjet && h.ObjetId == objetId)
            .OrderByDescending(h => h.Id)
            .Take(maximum)
            .ToListAsync();
        var prenoms = await db.Utilisateurs.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Prenom);
        return lignes
            .Select(h => new LigneHistorique(h.Date, h.UtilisateurId is { } id ? prenoms.GetValueOrDefault(id) : null, h.Action, h.Phrase))
            .ToList();
    }
}
