namespace Barthazon.Catalog.Contracts;

/// <summary>
/// Couleur d'une étiquette : une teinte de la charte (les valeurs réelles sont définies dans BrandTokens),
/// jamais une couleur libre.
/// </summary>
public enum TagColor
{
    Neutral = 0,
    Primary = 1,
    Accent = 2,
    Success = 3,
    Warning = 4,
    Error = 5,
    Info = 6,
}

/// <summary>Statut d'une pièce.</summary>
public enum PartStatus
{
    Prototype = 0,
    Active = 1,

    /// <summary>En sommeil.</summary>
    Dormant = 2,
    Obsolete = 3,
}

/// <summary>Statut d'un indice (version de pièce).</summary>
public enum RevisionStatus
{
    /// <summary>Brouillon.</summary>
    Draft = 0,

    /// <summary>En vigueur : indice à utiliser pour fabriquer.</summary>
    InForce = 1,

    /// <summary>Remplacé : ancien indice conservé pour mémoire.</summary>
    Superseded = 2,

    /// <summary>Annulé.</summary>
    Cancelled = 3,
}

/// <summary>Origine d'un changement d'indice.</summary>
public enum ChangeOrigin
{
    CustomerRequest = 0,
    Correction = 1,
    InternalImprovement = 2,
    Creation = 3,
    Other = 99,
}

/// <summary>Forme du brut.</summary>
public enum BlankShape
{
    Round = 0,
    Square = 1,
    Flat = 2,
    Hexagonal = 3,
    Tube = 4,
    Sheet = 5,
    Block = 6,
    CastOrForged = 7,
    Other = 99,
}

/// <summary>Type d'opération de gamme.</summary>
public enum OperationType
{
    Cutting = 0,
    Turning = 1,
    Milling = 2,
    DrillingTapping = 3,
    Grinding = 4,
    Deburring = 5,
    Treatment = 6,
    Inspection = 7,
    Subcontracting = 8,
    Other = 99,
}

/// <summary>Nature d'un document : fichier numérique ou papier seulement.</summary>
public enum DocumentKind
{
    File = 0,
    Paper = 1,
}

/// <summary>Type de document.</summary>
public enum DocumentType
{
    Drawing = 0,
    Model3D = 1,
    NcProgram = 2,
    InspectionSheet = 3,
    Photo = 4,
    Routing = 5,
    Certificate = 6,
    Other = 99,
}

/// <summary>Type de lien entre pièces.</summary>
public enum PartLinkType
{
    /// <summary>La pièce source fait partie de la pièce cible.</summary>
    ComponentOf = 0,

    /// <summary>La pièce source remplace la pièce cible.</summary>
    Replaces = 1,

    /// <summary>La pièce source est une variante de la pièce cible.</summary>
    Variant = 2,

    /// <summary>Pièces associées.</summary>
    Related = 3,
}
