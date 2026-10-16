using Barthazon.Catalog.Contracts;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Services;
using Barthazon.Stock.UI.Resources;

namespace Barthazon.Stock.UI.Components;

/// <summary>Libellés français des énumérations du stock (ressources StockTexts).</summary>
public static class StockLabels
{
    public static string Label(this StockItemKind value) => Get("ItemKind_" + value) ?? value.ToString();

    /// <summary>Symbole court de l'unité (u, m, kg, m²).</summary>
    public static string Symbol(this StockUnit value) => Get("Unit_" + value) ?? value.ToString();

    public static string Label(this StockUnit value) => Get("UnitName_" + value) ?? value.ToString();

    public static string Label(this StockLocationKind value) => Get("LocationKind_" + value) ?? value.ToString();

    public static string Label(this StockTransactionType value) => Get("TransactionType_" + value) ?? value.ToString();

    public static string Label(this StockReason value) => Get("Reason_" + value) ?? value.ToString();

    public static string Label(this BlankShape value) => Get("BlankShape_" + value) ?? value.ToString();

    public static string Label(this OperationType value) => Get("OperationType_" + value) ?? value.ToString();

    /// <summary>Quantité et unité (« 18 m », « 30 u »).</summary>
    public static string Quantity(decimal quantity, StockUnit unit) => $"{StockFormat.Quantity(quantity)} {unit.Symbol()}";

    /// <summary>Variation signée (« +18 m », « −4 u »).</summary>
    public static string Delta(decimal delta, StockUnit unit) =>
        (delta > 0 ? "+" : delta < 0 ? "−" : string.Empty) + Quantity(Math.Abs(delta), unit);

    private static string? Get(string key) => StockTexts.ResourceManager.GetString(key, StockTexts.Culture);
}
