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
