using System.Globalization;
using Barthazon.Stock.Core.Domain;

namespace Barthazon.Stock.Core.Services;

/// <summary>Mise en forme des quantités (culture courante, trois décimales au plus, sans zéros inutiles).</summary>
public static class StockFormat
{
    public static string Quantity(decimal quantity) => quantity.ToString("0.###", CultureInfo.CurrentCulture);

    /// <summary>Arrondi des quantités d'articles (trois décimales).</summary>
    public static decimal Round(decimal quantity) => Math.Round(quantity, 3, MidpointRounding.AwayFromZero);

    /// <summary>Vrai si la quantité respecte l'unité : entier pour des pièces, trois décimales au plus sinon.</summary>
    public static bool FitsUnit(decimal quantity, StockUnit unit) =>
        unit == StockUnit.Piece ? quantity == decimal.Truncate(quantity) : quantity == Round(quantity);
}
