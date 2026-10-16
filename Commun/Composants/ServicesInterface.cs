namespace Barthazon.Commun.Composants;

public enum GenreToast
{
    Succes,
    Info,
    Erreur,
}

public sealed record MessageToast(Guid Id, string Texte, GenreToast Genre);

/// <summary>Messages courts de retour immédiat (« Arrivage enregistré : +20 barres »).</summary>
public sealed class ServiceToast
{
    private readonly List<MessageToast> _messages = [];

    public event Action? Change;

    public IReadOnlyList<MessageToast> Messages => _messages;

    public void Succes(string texte) => Afficher(texte, GenreToast.Succes);

    public void Info(string texte) => Afficher(texte, GenreToast.Info);

    public void Erreur(string texte) => Afficher(texte, GenreToast.Erreur);

    public void Fermer(Guid id)
    {
        _messages.RemoveAll(m => m.Id == id);
        Change?.Invoke();
    }

    private void Afficher(string texte, GenreToast genre)
    {
        var message = new MessageToast(Guid.NewGuid(), texte, genre);
        _messages.Add(message);
        if (_messages.Count > 3)
        {
            _messages.RemoveAt(0);
        }

        Change?.Invoke();
        _ = FermerPlusTard(message.Id, genre == GenreToast.Erreur ? 7000 : 4000);
    }

    private async Task FermerPlusTard(Guid id, int millisecondes)
    {
        await Task.Delay(millisecondes);
        Fermer(id);
    }
}

public sealed record DemandeConfirmation(string Question, string Valider, string Annuler, bool Danger, TaskCompletionSource<bool> Reponse);

/// <summary>
/// Confirmation en une phrase avant une action destructrice ou inhabituelle :
/// <c>if (await Confirmation.Demander("Le stock passera à −3 barres. Continuer ?")) …</c>
/// </summary>
public sealed class ServiceConfirmation
{
    public event Action? Change;

    public DemandeConfirmation? EnCours { get; private set; }

    public Task<bool> Demander(string question, string valider = "Continuer", string annuler = "Annuler", bool danger = false)
    {
        EnCours?.Reponse.TrySetResult(false);
        EnCours = new DemandeConfirmation(question, valider, annuler, danger, new TaskCompletionSource<bool>());
        Change?.Invoke();
        return EnCours.Reponse.Task;
    }

    public void Repondre(bool oui)
    {
        var demande = EnCours;
        EnCours = null;
        Change?.Invoke();
        demande?.Reponse.TrySetResult(oui);
    }
}

/// <summary>
/// Signal partagé par tous les utilisateurs : une application prévient qu'une donnée a changé
/// (ex. « stock ») pour que les compteurs affichés ailleurs se mettent à jour.
/// </summary>
public sealed class SignauxApplication
{
    public event Action<string>? Signal;

    public void Emettre(string sujet) => Signal?.Invoke(sujet);
}
