using System.Globalization;
using Barthazon.Stock.Core.Resources;
using Microsoft.Extensions.Configuration;

namespace Barthazon.Stock.Core;

/// <summary>Section « Stock » de la configuration (section 13 de la spécification).</summary>
public sealed class StockOptions
{
    public const string SectionName = "Stock";

    /// <summary>Modèle de code d'article ({0} = numéro).</summary>
    public string ItemReferencePattern { get; set; } = "A-{0:0000}";

    /// <summary>Modèle de numéro de lancement ({0} = date du lancement, {1} = séquence annuelle).</summary>
    public string BatchNumberPattern { get; set; } = "L{0:yyyy}-{1:000}";

    public string DefaultStorageLocationCode { get; set; } = "MAG";

    public string DefaultWorkInProgressLocationCode { get; set; } = "ATELIER";

    public bool AllowNegativeItemStock { get; set; }

    /// <summary>Antidatage maximal d'un mouvement, en jours.</summary>
    public int BackdatingDays { get; set; } = 31;

    /// <summary>Délai pendant lequel l'auteur d'un mouvement peut l'annuler lui-même, en minutes.</summary>
    public int SelfUndoMinutes { get; set; } = 15;

    /// <summary>Un lancement sans mouvement depuis ce nombre de jours est signalé « à l'arrêt ».</summary>
    public int StaleBatchDays { get; set; } = 14;

    public static StockOptions From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(SectionName);
        var defaults = new StockOptions();
        return new StockOptions
        {
            ItemReferencePattern = section[nameof(ItemReferencePattern)] ?? defaults.ItemReferencePattern,
            BatchNumberPattern = section[nameof(BatchNumberPattern)] ?? defaults.BatchNumberPattern,
            DefaultStorageLocationCode = section[nameof(DefaultStorageLocationCode)] ?? defaults.DefaultStorageLocationCode,
            DefaultWorkInProgressLocationCode = section[nameof(DefaultWorkInProgressLocationCode)] ?? defaults.DefaultWorkInProgressLocationCode,
            AllowNegativeItemStock = ReadBool(section, nameof(AllowNegativeItemStock), defaults.AllowNegativeItemStock),
            BackdatingDays = ReadInt(section, nameof(BackdatingDays), defaults.BackdatingDays),
            SelfUndoMinutes = ReadInt(section, nameof(SelfUndoMinutes), defaults.SelfUndoMinutes),
            StaleBatchDays = ReadInt(section, nameof(StaleBatchDays), defaults.StaleBatchDays),
        };
    }

    /// <summary>Erreurs de configuration (messages en français), vides si tout est correct.</summary>
    public static IReadOnlyList<string> Validate(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var section = configuration.GetSection(SectionName);
        var errors = new List<string>();
        foreach (var key in new[] { nameof(BackdatingDays), nameof(SelfUndoMinutes), nameof(StaleBatchDays) })
        {
            if (section[key] is { } raw && !int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
            {
                errors.Add(string.Format(CultureInfo.CurrentCulture, StockMessages.ConfigNumberInvalid, SectionName + ":" + key, raw));
            }
        }

        if (section[nameof(AllowNegativeItemStock)] is { } flag && !bool.TryParse(flag, out _))
        {
            errors.Add(string.Format(CultureInfo.CurrentCulture, StockMessages.ConfigBooleanInvalid, SectionName + ":" + nameof(AllowNegativeItemStock), flag));
        }

        var options = From(configuration);
        if (!IsValidPattern(options.ItemReferencePattern, 1))
        {
            errors.Add(string.Format(CultureInfo.CurrentCulture, StockMessages.ConfigItemPatternInvalid, options.ItemReferencePattern));
        }

        if (!IsValidPattern(options.BatchNumberPattern, 2))
        {
            errors.Add(string.Format(CultureInfo.CurrentCulture, StockMessages.ConfigBatchPatternInvalid, options.BatchNumberPattern));
        }

        if (string.IsNullOrWhiteSpace(options.DefaultStorageLocationCode) || string.IsNullOrWhiteSpace(options.DefaultWorkInProgressLocationCode))
        {
            errors.Add(StockMessages.ConfigDefaultLocationMissing);
        }

        if (options.BackdatingDays < 0 || options.SelfUndoMinutes < 0 || options.StaleBatchDays <= 0)
        {
            errors.Add(StockMessages.ConfigNegativeNumber);
        }

        return errors;
    }

    /// <summary>
    /// Un modèle est valide s'il se met en forme sans erreur et produit des valeurs différentes pour deux numéros différents.
    /// </summary>
    private static bool IsValidPattern(string? pattern, int arguments)
    {
        if (string.IsNullOrWhiteSpace(pattern) || pattern.Length > 40)
        {
            return false;
        }

        try
        {
            var date = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);
            object[] first = arguments == 1 ? [1] : [date, 1];
            object[] second = arguments == 1 ? [2] : [date, 2];
            return string.Format(CultureInfo.InvariantCulture, pattern, first) != string.Format(CultureInfo.InvariantCulture, pattern, second);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static int ReadInt(IConfiguration section, string key, int fallback) =>
        int.TryParse(section[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static bool ReadBool(IConfiguration section, string key, bool fallback) =>
        bool.TryParse(section[key], out var value) ? value : fallback;
}
