namespace Barthazon.Commun.Outils;

/// <summary>Résultat d'une opération de service : succès, ou message d'erreur lisible par l'utilisateur.</summary>
public class Resultat
{
    protected Resultat(bool reussi, string? erreur)
    {
        Reussi = reussi;
        Erreur = erreur;
    }

    public bool Reussi { get; }

    public string? Erreur { get; }

    public static Resultat Ok() => new(true, null);

    public static Resultat Echec(string erreur) => new(false, erreur);

    public static Resultat<T> Ok<T>(T valeur) => new(valeur);

    public static Resultat<T> Echec<T>(string erreur) => new(erreur);

    /// <summary>Message renvoyé quand l'utilisateur n'a pas le rôle nécessaire.</summary>
    public const string NonAutorise = "Vous n'avez pas les droits pour cette action.";
}

public sealed class Resultat<T> : Resultat
{
    internal Resultat(T valeur)
        : base(true, null) => Valeur = valeur;

    internal Resultat(string erreur)
        : base(false, erreur) => Valeur = default;

    public T? Valeur { get; }

    public static implicit operator Resultat<T>(T valeur) => new(valeur);
}
