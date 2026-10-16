using Barthazon.Commun.Contrats;
using Barthazon.Stock.Donnees;

namespace Barthazon.Stock.Services;

/// <summary>Règles de calcul du stock (sans accès aux données).</summary>
public static class ReglesStock
{
    /// <summary>
    /// Rupture si la quantité est ≤ 0, Sous le seuil si elle est ≤ au seuil, sinon OK.
    /// Une référence jamais mouvementée reste neutre (pour ne pas noyer les alertes au démarrage).
    /// </summary>
    public static EtatStock Etat(decimal quantite, decimal? seuil, bool mouvemente)
    {
        if (!mouvemente)
        {
            return EtatStock.Neutre;
        }

        if (quantite <= 0)
        {
            return EtatStock.Rupture;
        }

        return seuil is { } s && quantite <= s ? EtatStock.SousLeSeuil : EtatStock.Ok;
    }

    public static EtatStock Etat(NiveauStock? niveau) =>
        niveau is null ? EtatStock.Neutre : Etat(niveau.Quantite, niveau.SeuilAlerte, niveau.DernierMouvement is not null);

    /// <summary>Pièces obtenues pour un nombre de bruts consommés (et inversement), selon « pièces obtenues par brut ».</summary>
    public static decimal PiecesPourBruts(decimal bruts, int piecesParBrut) => bruts * Math.Max(1, piecesParBrut);

    public static decimal BrutsPourPieces(decimal pieces, int piecesParBrut) => Math.Ceiling(pieces / Math.Max(1, piecesParBrut));

    /// <summary>Motifs de sortie proposés : pièce finie (livraison par défaut) ou brut (rebut par défaut).</summary>
    public static IReadOnlyList<MotifSortie> Motifs(TypeReference type) => type == TypeReference.PieceFinie
        ? [MotifSortie.LivraisonClient, MotifSortie.Rebut, MotifSortie.Autre]
        : [MotifSortie.Rebut, MotifSortie.HorsFabrication, MotifSortie.RetourFournisseur, MotifSortie.Autre];

    public static string Libelle(MotifSortie motif) => motif switch
    {
        MotifSortie.LivraisonClient => "Livraison client",
        MotifSortie.Rebut => "Rebut",
        MotifSortie.HorsFabrication => "Utilisé hors fabrication",
        MotifSortie.RetourFournisseur => "Retour fournisseur",
        _ => "Autre",
    };

    public static string Libelle(TypeMouvement type) => type switch
    {
        TypeMouvement.Arrivage => "Arrivage",
        TypeMouvement.Fabrication => "Fabrication",
        TypeMouvement.Sortie => "Sortie",
        TypeMouvement.Ajustement => "Ajustement",
        TypeMouvement.Annulation => "Annulation",
        _ => type.ToString(),
    };
}
