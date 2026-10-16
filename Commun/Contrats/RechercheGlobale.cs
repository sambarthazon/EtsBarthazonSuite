namespace Barthazon.Commun.Contrats;

/// <summary>Un résultat de la recherche globale de la barre du haut.</summary>
/// <param name="Groupe">Groupe d'affichage : « Pièces finies », « Bruts », « Clients », « Fournisseurs ».</param>
/// <param name="OrdreGroupe">Ordre du groupe dans la liste.</param>
public sealed record ResultatRecherche(string Groupe, int OrdreGroupe, string TypeObjet, int Id, string Titre, string? Detail);

/// <summary>Fournisseur de résultats pour la recherche globale (implémenté par les applications).</summary>
public interface IRechercheGlobale
{
    /// <summary>Au plus <paramref name="maximumParGroupe"/> résultats par groupe.</summary>
    Task<IReadOnlyList<ResultatRecherche>> Rechercher(string texte, int maximumParGroupe);
}
