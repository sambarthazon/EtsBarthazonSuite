namespace Barthazon.Stock.UI.Components;

/// <summary>Actions de saisie hors lancement (saisie rapide, fiche article, page d'un emplacement).</summary>
public enum StockAction
{
    Receipt,
    Issue,
    Transfer,
    Adjustment,

    /// <summary>Sortie de pièces finies (livraison).</summary>
    Shipment,

    /// <summary>Entrée de pièces finies (retour client, stock initial).</summary>
    PartReceipt,
}
