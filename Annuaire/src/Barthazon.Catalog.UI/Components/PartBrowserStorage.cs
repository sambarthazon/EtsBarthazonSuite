using Barthazon.Shared.UI.Services;

namespace Barthazon.Catalog.UI.Components;

/// <summary>
/// Listes de références mémorisées dans le navigateur (par appareil) : pièces consultées récemment,
/// et favoris des visiteurs non connectés.
/// </summary>
public static class PartBrowserStorage
{
    public const string RecentKey = "bz-recent-parts";

    public const string FavoritesKey = "bz-favorite-parts";

    public const int MaxRecent = 12;

    public static async Task<IReadOnlyList<string>> ReadAsync(BrowserInterop browser, string key)
    {
        ArgumentNullException.ThrowIfNull(browser);
        var stored = await browser.GetStorageAsync(key) ?? string.Empty;
        return stored.Split('|', StringSplitOptions.RemoveEmptyEntries);
    }

    public static ValueTask WriteAsync(BrowserInterop browser, string key, IEnumerable<string> references)
    {
        ArgumentNullException.ThrowIfNull(browser);
        return browser.SetStorageAsync(key, string.Join('|', references));
    }

    /// <summary>Place la référence en tête de liste (sans doublon).</summary>
    public static async Task PushAsync(BrowserInterop browser, string key, string reference, int max = MaxRecent)
    {
        var current = await ReadAsync(browser, key);
        await WriteAsync(browser, key, current.Where(r => !string.Equals(r, reference, StringComparison.OrdinalIgnoreCase)).Prepend(reference).Take(max));
    }

    public static async Task RemoveAsync(BrowserInterop browser, string key, string reference)
    {
        var current = await ReadAsync(browser, key);
        await WriteAsync(browser, key, current.Where(r => !string.Equals(r, reference, StringComparison.OrdinalIgnoreCase)));
    }
}
