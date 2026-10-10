using Microsoft.JSInterop;

namespace Barthazon.Shared.UI.Services;

/// <summary>Petites fonctions navigateur (voir wwwroot/barthazon.js). Les erreurs JS sont ignorées (prérendu, déconnexion).</summary>
public sealed class BrowserInterop(IJSRuntime js)
{
    public ValueTask SetThemeAsync(bool dark) => SafeAsync("barthazon.setTheme", dark ? "dark" : "light");

    public ValueTask RegisterShortcutsAsync() => SafeAsync("barthazon.registerShortcuts");

    /// <summary>Fait télécharger un fichier produit par le serveur (export, modèle).</summary>
    public async ValueTask DownloadAsync(string fileName, byte[] content)
    {
        using var stream = new MemoryStream(content);
        using var reference = new DotNetStreamReference(stream);
        await SafeAsync("barthazon.downloadFromStream", fileName, reference);
    }

    public ValueTask PrintAsync() => SafeAsync("barthazon.print");

    public ValueTask FocusAsync(string elementId) => SafeAsync("barthazon.focus", elementId);

    public ValueTask SetStorageAsync(string key, string? value) => SafeAsync("barthazon.storageSet", key, value);

    public async ValueTask<string?> GetStorageAsync(string key)
    {
        try
        {
            return await js.InvokeAsync<string?>("barthazon.storageGet", key);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or JSDisconnectedException or TaskCanceledException)
        {
            return null;
        }
    }

    public async ValueTask<bool?> PrefersDarkAsync()
    {
        try
        {
            return await js.InvokeAsync<bool>("barthazon.prefersDark");
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or JSDisconnectedException or TaskCanceledException)
        {
            return null;
        }
    }

    private async ValueTask SafeAsync(string identifier, params object?[] args)
    {
        try
        {
            await js.InvokeVoidAsync(identifier, args);
        }
        catch (Exception ex) when (ex is JSException or InvalidOperationException or JSDisconnectedException or TaskCanceledException)
        {
            // Appel JS impossible (prérendu ou circuit fermé) : sans conséquence.
        }
    }
}
