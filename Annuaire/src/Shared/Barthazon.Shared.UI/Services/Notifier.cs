using Barthazon.Shared.Kernel.Results;
using MudBlazor;

namespace Barthazon.Shared.UI.Services;

/// <summary>Notifications discrètes (succès / erreurs métier).</summary>
public sealed class Notifier(ISnackbar snackbar)
{
    public void Success(string message) => snackbar.Add(message, Severity.Success);

    public void Info(string message) => snackbar.Add(message, Severity.Info);

    public void Warning(string message) => snackbar.Add(message, Severity.Warning);

    public void Error(string message) => snackbar.Add(message, Severity.Error);

    /// <summary>Affiche les erreurs d'un résultat en échec. Renvoie vrai si le résultat est un succès.</summary>
    public bool Handle(Result result, string? successMessage = null)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (result.IsSuccess)
        {
            if (successMessage is not null)
            {
                Success(successMessage);
            }

            return true;
        }

        foreach (var error in result.Errors.Where(e => e.Field is null).DefaultIfEmpty(result.Errors[0]))
        {
            snackbar.Add(error.Message, error.Kind == ErrorKind.Conflict ? Severity.Warning : Severity.Error);
        }

        return false;
    }
}
