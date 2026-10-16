using System.Security.Claims;

namespace Barthazon.Commun.Securite;

/// <summary>Rôles, du plus restreint au plus large : chaque rôle a les droits des précédents.</summary>
public enum Role
{
    /// <summary>Tout voir, rechercher, imprimer, télécharger les plans.</summary>
    Consultation = 1,

    /// <summary>+ enregistrer des mouvements de stock.</summary>
    Atelier = 2,

    /// <summary>+ créer / modifier / archiver ; nouvel indice ; ajustements ; seuils ; annuler tout mouvement.</summary>
    Gestion = 3,

    /// <summary>+ utilisateurs, listes paramétrables, thème.</summary>
    Administrateur = 4,
}

public static class Roles
{
    public static string Libelle(Role role) => role switch
    {
        Role.Consultation => "Consultation",
        Role.Atelier => "Atelier",
        Role.Gestion => "Gestion",
        Role.Administrateur => "Administrateur",
        _ => role.ToString(),
    };

    public static string Description(Role role) => role switch
    {
        Role.Consultation => "Voir, rechercher, imprimer",
        Role.Atelier => "+ arrivages, fabrication, sorties",
        Role.Gestion => "+ créer et modifier les fiches, inventaire",
        Role.Administrateur => "+ utilisateurs et paramètres",
        _ => string.Empty,
    };
}

/// <summary>Utilisateur connecté, disponible dans tous les services.</summary>
public interface IUtilisateurCourant
{
    bool EstConnecte { get; }

    int Id { get; }

    string Prenom { get; }

    string Initiales { get; }

    Role Role { get; }

    /// <summary>Connexion depuis un appareil partagé (déconnexion après inactivité).</summary>
    bool AppareilPartage { get; }

    /// <summary>Vrai si l'utilisateur est connecté et a au moins ce rôle.</summary>
    bool Peut(Role role);
}

/// <summary>Noms des revendications du cookie de connexion.</summary>
public static class Revendications
{
    public const string Initiales = "bz:initiales";
    public const string Partage = "bz:partage";

    /// <summary>Fin absolue de la session (ticks UTC).</summary>
    public const string Fin = "bz:fin";
}

/// <summary>Alimenté par le middleware (requêtes HTTP) et par le gestionnaire de circuit (Blazor).</summary>
public sealed class UtilisateurCourant : IUtilisateurCourant
{
    private ClaimsPrincipal _principal = new(new ClaimsIdentity());

    public bool EstConnecte => _principal.Identity?.IsAuthenticated == true && Id > 0;

    public int Id => int.TryParse(_principal.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : 0;

    public string Prenom => _principal.FindFirstValue(ClaimTypes.Name) ?? string.Empty;

    public string Initiales => _principal.FindFirstValue(Revendications.Initiales) ?? string.Empty;

    public Role Role => Enum.TryParse<Role>(_principal.FindFirstValue(ClaimTypes.Role), out var role) ? role : 0;

    public bool AppareilPartage => _principal.FindFirstValue(Revendications.Partage) == "1";

    public bool Peut(Role role) => EstConnecte && Role >= role;

    public void Definir(ClaimsPrincipal principal) => _principal = principal ?? new ClaimsPrincipal(new ClaimsIdentity());
}
