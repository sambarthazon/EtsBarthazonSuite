using MudBlazor;

namespace Barthazon.Shared.UI.Components;

public static class DialogServiceExtensions
{
    /// <summary>Affiche une confirmation ; renvoie vrai si l'utilisateur confirme.</summary>
    public static async Task<bool> ConfirmAsync(this IDialogService dialogs, string title, string message, string? confirmText = null, bool danger = false)
    {
        ArgumentNullException.ThrowIfNull(dialogs);
        var parameters = new DialogParameters<ConfirmDialog>
        {
            { d => d.Title, title },
            { d => d.Message, message },
            { d => d.ConfirmText, confirmText },
            { d => d.Danger, danger },
        };
        var dialog = await dialogs.ShowAsync<ConfirmDialog>(title, parameters, new DialogOptions { MaxWidth = MaxWidth.Small, FullWidth = true });
        var result = await dialog.Result;
        return result is { Canceled: false };
    }
}
