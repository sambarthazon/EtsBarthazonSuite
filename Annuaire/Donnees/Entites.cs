using Barthazon.Commun.Contrats;

namespace Barthazon.Annuaire.Donnees;

public sealed class Client
{
    public int Id { get; set; }

    public string Nom { get; set; } = string.Empty;

    public string? Contact { get; set; }

    public string? Telephone { get; set; }

    public string? Email { get; set; }

    public string? Remarques { get; set; }

    public bool Archive { get; set; }
}

public sealed class Fournisseur
{
    public int Id { get; set; }

    public string Nom { get; set; } = string.Empty;

    public string? Contact { get; set; }

    public string? Telephone { get; set; }

    public string? Email { get; set; }

    public string? Remarques { get; set; }

    public bool Archive { get; set; }
}

/// <summary>Liste paramétrable des matières (S235, C45, 42CrMo4…).</summary>
public sealed class Matiere
{
    public int Id { get; set; }

    public string Libelle { get; set; } = string.Empty;
}

/// <summary>Liste paramétrable des postes de gamme (Tour, Fraiseuse…).</summary>
public sealed class Poste
{
    public int Id { get; set; }

    public string Libelle { get; set; } = string.Empty;

    public int Ordre { get; set; }
}

/// <summary>Matière ou ébauche reçue du fournisseur : porte les caractéristiques physiques et d'approvisionnement.</summary>
public sealed class Brut
{
    public int Id { get; set; }

    /// <summary>Référence interne automatique : BR-0001, BR-0002…</summary>
    public string Reference { get; set; } = string.Empty;

    public FormeBrut Forme { get; set; }

    public decimal? Diametre { get; set; }

    public decimal? DiametreInterieur { get; set; }

    public decimal? Largeur { get; set; }

    public decimal? Epaisseur { get; set; }

    public decimal? Cote { get; set; }

    public decimal? SurPlats { get; set; }

    public decimal? Longueur { get; set; }

    public string? DescriptionForme { get; set; }

    public int MatiereId { get; set; }

    public Matiere? Matiere { get; set; }

    public Unite Unite { get; set; } = Unite.Piece;

    public string? Designation { get; set; }

    public string? Remarques { get; set; }

    public bool Archive { get; set; }

    public List<BrutFournisseur> Fournisseurs { get; set; } = [];
}

public sealed class BrutFournisseur
{
    public int BrutId { get; set; }

    public int FournisseurId { get; set; }

    public Fournisseur? Fournisseur { get; set; }

    public string? ReferenceFournisseur { get; set; }

    public bool Principal { get; set; }
}

/// <summary>Pièce usinée livrée au client, définie par le plan du client.</summary>
public sealed class PieceFinie
{
    public int Id { get; set; }

    /// <summary>En général le n° de plan du client ; unique pour un même client.</summary>
    public string Reference { get; set; } = string.Empty;

    public string Designation { get; set; } = string.Empty;

    public int ClientId { get; set; }

    public Client? Client { get; set; }

    public int? IndiceCourantId { get; set; }

    public string? EmplacementPlanPapier { get; set; }

    public string? Remarques { get; set; }

    public bool Archive { get; set; }

    public List<PieceFinieBrut> Bruts { get; set; } = [];

    public List<Indice> Indices { get; set; } = [];
}

/// <summary>Brut possible pour une pièce finie.</summary>
public sealed class PieceFinieBrut
{
    public int PieceFinieId { get; set; }

    public int BrutId { get; set; }

    public Brut? Brut { get; set; }

    /// <summary>Un seul brut principal par pièce : proposé par défaut partout.</summary>
    public bool Principal { get; set; }

    /// <summary>Pièces obtenues par brut (ex. une barre de 1 m donne 8 pièces).</summary>
    public int PiecesParBrut { get; set; } = 1;
}

public sealed class Indice
{
    public int Id { get; set; }

    public int PieceFinieId { get; set; }

    public string Code { get; set; } = "A";

    public DateTime Date { get; set; }

    public int? AuteurId { get; set; }

    /// <summary>Ce qui a changé par rapport à l'indice précédent.</summary>
    public string? DescriptionChangement { get; set; }

    public Specification? Specification { get; set; }

    public List<SpecificationLigne> LignesSpecification { get; set; } = [];

    public List<Plan> Plans { get; set; } = [];

    public List<OperationGamme> Operations { get; set; } = [];
}

public sealed class Specification
{
    public int Id { get; set; }

    public int IndiceId { get; set; }

    public string? TraitementThermique { get; set; }

    public string? TraitementSurface { get; set; }

    public string? ToleranceGenerale { get; set; }

    public string? Masse { get; set; }

    public string? Remarques { get; set; }
}

/// <summary>Spécification libre (libellé / valeur).</summary>
public sealed class SpecificationLigne
{
    public int Id { get; set; }

    public int IndiceId { get; set; }

    public string Libelle { get; set; } = string.Empty;

    public string Valeur { get; set; } = string.Empty;

    public int Ordre { get; set; }
}

public sealed class Plan
{
    public int Id { get; set; }

    public int IndiceId { get; set; }

    public string NomFichier { get; set; } = string.Empty;

    /// <summary>Chemin relatif au dossier des plans : {pieceFinieId}/{indice}/{fichier}.</summary>
    public string Chemin { get; set; } = string.Empty;

    public string TypeMime { get; set; } = string.Empty;

    public long Taille { get; set; }

    public DateTime DateAjout { get; set; }

    public int? AuteurId { get; set; }
}

public sealed class OperationGamme
{
    public int Id { get; set; }

    public int IndiceId { get; set; }

    /// <summary>N° d'opération : 10, 20, 30…</summary>
    public int Numero { get; set; }

    public int? PosteId { get; set; }

    public Poste? Poste { get; set; }

    public string Description { get; set; } = string.Empty;

    public int? TempsEstimeMin { get; set; }

    public string? Remarque { get; set; }

    public int Ordre { get; set; }
}

public enum TypeLien
{
    Remplace = 1,
    RemplaceePar = 2,
    MemeFamille = 3,
    SeMonteAvec = 4,
    Externe = 5,
}

/// <summary>Lien entre pièces finies, ou vers l'extérieur.</summary>
public sealed class Lien
{
    public int Id { get; set; }

    public int PieceFinieId { get; set; }

    public TypeLien Type { get; set; }

    public int? PieceCibleId { get; set; }

    public string? Url { get; set; }

    public string? Libelle { get; set; }
}
