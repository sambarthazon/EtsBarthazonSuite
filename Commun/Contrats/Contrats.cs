namespace Barthazon.Commun.Contrats;

// Contrats d'échange entre applications : interfaces et DTO.
// Une application n'utilise une autre que par ces contrats, jamais par référence directe.
// Ils n'évoluent que par ajout (nouveau membre ou nouvelle interface, jamais de changement de signature).

/// <summary>Sorte de chose suivie en stock.</summary>
public enum TypeReference
{
    Brut = 1,
    PieceFinie = 2,
}

/// <summary>État d'un niveau de stock (badge).</summary>
public enum EtatStock
{
    /// <summary>Jamais mouvementé : gris, pour ne pas noyer les alertes au démarrage.</summary>
    Neutre = 0,
    Ok = 1,
    SousLeSeuil = 2,
    Rupture = 3,
}

/// <summary>Unité de stock d'un brut.</summary>
public enum Unite
{
    Piece = 1,
    Barre = 2,
    Lopin = 3,
    Metre = 4,
    Kg = 5,
}

/// <summary>Forme d'un brut.</summary>
public enum FormeBrut
{
    RondPlein = 1,
    Tube = 2,
    Plat = 3,
    Carre = 4,
    Hexagone = 5,
    Tole = 6,
    MouleeForgee = 7,
    Autre = 8,
}

/// <summary>Types d'objets échangés entre applications (bascule contextuelle, recherche, historique).</summary>
public static class TypesObjet
{
    public const string PieceFinie = "piece-finie";
    public const string Brut = "brut";
    public const string Client = "client";
    public const string Fournisseur = "fournisseur";
}

public sealed record BrutResume(
    int Id,
    string Reference,
    string Libelle,
    FormeBrut Forme,
    int MatiereId,
    string Matiere,
    Unite Unite,
    int? FournisseurPrincipalId,
    string? FournisseurPrincipal,
    bool Archive);

public sealed record PieceFinieResume(
    int Id,
    string Reference,
    string Designation,
    int ClientId,
    string Client,
    string Indice,
    bool Archive);

/// <summary>Association d'un brut possible à une pièce finie.</summary>
public sealed record BrutAssocie(int PieceFinieId, BrutResume Brut, bool Principal, int PiecesParBrut);

public sealed record FournisseurBrut(int FournisseurId, string Nom, string? ReferenceFournisseur, bool Principal);

/// <summary>Client ou fournisseur (nom seulement).</summary>
public sealed record TiersResume(int Id, string Nom);

/// <summary>Aperçu du stock d'un brut ou d'une pièce finie, affiché dans l'Annuaire.</summary>
public sealed record ApercuStock(
    decimal Quantite,
    string QuantiteTexte,
    EtatStock Etat,
    decimal? Seuil,
    DateTime? DernierMouvement,
    string? DernierMouvementTexte);

/// <summary>Implémenté par l'Annuaire, utilisé par le Stock.</summary>
public interface IAnnuaireLecture
{
    Task<IReadOnlyList<PieceFinieResume>> ListerPiecesFinies(bool inclureArchivees = false);

    Task<PieceFinieResume?> ObtenirPieceFinie(int id);

    Task<IReadOnlyList<BrutResume>> ListerBruts(bool inclureArchives = false);

    Task<BrutResume?> ObtenirBrut(int id);

    /// <summary>Bruts possibles d'une pièce finie, avec Principal et PiecesParBrut (principal en premier).</summary>
    Task<IReadOnlyList<BrutAssocie>> BrutsDeLaPiece(int pieceFinieId);

    Task<IReadOnlyList<FournisseurBrut>> FournisseursDuBrut(int brutId);

    /// <summary>Toutes les associations pièce finie ↔ brut (pour les listes, sans requête par ligne).</summary>
    Task<IReadOnlyList<BrutAssocie>> ListerAssociations();

    Task<IReadOnlyList<TiersResume>> ListerClients();

    Task<IReadOnlyList<TiersResume>> ListerFournisseurs();
}

/// <summary>Implémenté par le Stock, utilisé par l'Annuaire.</summary>
public interface IStockLecture
{
    /// <summary>Quantité, état, dernier mouvement ; <c>null</c> si le Stock n'est pas chargé.</summary>
    Task<ApercuStock?> ApercuPieceFinie(int pieceFinieId);

    /// <summary>Quantité, état, dernier arrivage ; <c>null</c> si le Stock n'est pas chargé.</summary>
    Task<ApercuStock?> ApercuBrut(int brutId);

    Task<bool> ADesMouvements(TypeReference type, int id);

    Task<int> NombreAlertes();
}

/// <summary>Implémentation vide : utilisée quand l'Annuaire n'est pas chargé.</summary>
internal sealed class AnnuaireLectureVide : IAnnuaireLecture
{
    public Task<IReadOnlyList<PieceFinieResume>> ListerPiecesFinies(bool inclureArchivees = false) => Task.FromResult<IReadOnlyList<PieceFinieResume>>([]);

    public Task<PieceFinieResume?> ObtenirPieceFinie(int id) => Task.FromResult<PieceFinieResume?>(null);

    public Task<IReadOnlyList<BrutResume>> ListerBruts(bool inclureArchives = false) => Task.FromResult<IReadOnlyList<BrutResume>>([]);

    public Task<BrutResume?> ObtenirBrut(int id) => Task.FromResult<BrutResume?>(null);

    public Task<IReadOnlyList<BrutAssocie>> BrutsDeLaPiece(int pieceFinieId) => Task.FromResult<IReadOnlyList<BrutAssocie>>([]);

    public Task<IReadOnlyList<FournisseurBrut>> FournisseursDuBrut(int brutId) => Task.FromResult<IReadOnlyList<FournisseurBrut>>([]);

    public Task<IReadOnlyList<BrutAssocie>> ListerAssociations() => Task.FromResult<IReadOnlyList<BrutAssocie>>([]);

    public Task<IReadOnlyList<TiersResume>> ListerClients() => Task.FromResult<IReadOnlyList<TiersResume>>([]);

    public Task<IReadOnlyList<TiersResume>> ListerFournisseurs() => Task.FromResult<IReadOnlyList<TiersResume>>([]);
}

/// <summary>Implémentation vide : utilisée quand le Stock n'est pas chargé (les blocs stock sont masqués).</summary>
internal sealed class StockLectureVide : IStockLecture
{
    public Task<ApercuStock?> ApercuPieceFinie(int pieceFinieId) => Task.FromResult<ApercuStock?>(null);

    public Task<ApercuStock?> ApercuBrut(int brutId) => Task.FromResult<ApercuStock?>(null);

    public Task<bool> ADesMouvements(TypeReference type, int id) => Task.FromResult(false);

    public Task<int> NombreAlertes() => Task.FromResult(0);
}
