using System.Text.RegularExpressions;
using Barthazon.Commun.Donnees;
using Barthazon.Commun.Outils;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Commun.Securite;

/// <summary>Tuile de l'écran de connexion.</summary>
public sealed record TuileUtilisateur(int Id, string Prenom, string Nom, string Initiales, int Couleur);

/// <summary>Utilisateur dont le PIN vient d'être vérifié.</summary>
public sealed record UtilisateurConnecte(int Id, string Prenom, string Initiales, Role Role, string? DerniereApplication);

/// <summary>Ligne de la page Paramètres → Utilisateurs.</summary>
public sealed record UtilisateurAdmin(int Id, string Prenom, string Nom, string Initiales, Role Role, bool Actif, bool Bloque, int Couleur);

/// <summary>Saisie de création ou de modification d'un utilisateur (PIN facultatif en modification).</summary>
public sealed record SaisieUtilisateur(string Prenom, string Nom, Role Role, bool Actif, string? Pin);

public sealed partial class ServiceUtilisateurs(
    IDbContextFactory<CommunDbContext> fabrique,
    IUtilisateurCourant courant,
    TimeProvider horloge)
{
    public const int EchecsAvantBlocage = 5;
    public static readonly TimeSpan DureeBlocage = TimeSpan.FromMinutes(1);

    private static readonly PasswordHasher<Utilisateur> Hacheur = new();

    /// <summary>Nombre de couleurs d'avatar définies dans theme.css (--avatar-0 … --avatar-7).</summary>
    public const int NombreCouleurs = 8;

    public static int CouleurAvatar(int id) => Math.Abs(id) % NombreCouleurs;

    public async Task<bool> ExisteUtilisateur()
    {
        await using var db = await fabrique.CreateDbContextAsync();
        return await db.Utilisateurs.AnyAsync();
    }

    public async Task<IReadOnlyList<TuileUtilisateur>> ListerTuiles()
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var actifs = await db.Utilisateurs.AsNoTracking().Where(u => u.Actif).ToListAsync();
        return actifs
            .OrderBy(u => u.Prenom, StringComparer.Create(Format.Culture, true))
            .ThenBy(u => u.Nom, StringComparer.Create(Format.Culture, true))
            .Select(u => new TuileUtilisateur(u.Id, u.Prenom, u.Nom, u.Initiales, CouleurAvatar(u.Id)))
            .ToList();
    }

    /// <summary>Vérifie le PIN ; bloque le compte une minute après 5 erreurs.</summary>
    public async Task<Resultat<UtilisateurConnecte>> VerifierPin(int id, string? pin)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var utilisateur = await db.Utilisateurs.FirstOrDefaultAsync(u => u.Id == id && u.Actif);
        if (utilisateur is null)
        {
            return Resultat.Echec<UtilisateurConnecte>("Utilisateur inconnu.");
        }

        var maintenant = horloge.GetUtcNow().UtcDateTime;
        if (utilisateur.BloqueJusqua is { } bloque && bloque > maintenant)
        {
            var secondes = (int)Math.Ceiling((bloque - maintenant).TotalSeconds);
            return Resultat.Echec<UtilisateurConnecte>($"Trop d'erreurs : réessayez dans {secondes} s.");
        }

        var correct = !string.IsNullOrEmpty(pin)
            && Hacheur.VerifyHashedPassword(utilisateur, utilisateur.PinHash, pin) != PasswordVerificationResult.Failed;
        if (!correct)
        {
            utilisateur.EchecsPin++;
            string message;
            if (utilisateur.EchecsPin >= EchecsAvantBlocage)
            {
                utilisateur.EchecsPin = 0;
                utilisateur.BloqueJusqua = maintenant + DureeBlocage;
                message = "Trop d'erreurs : compte bloqué 1 minute.";
            }
            else
            {
                var restants = EchecsAvantBlocage - utilisateur.EchecsPin;
                message = restants == 1 ? "Code incorrect. Dernier essai avant blocage." : $"Code incorrect. Encore {restants} essais.";
            }

            await db.SaveChangesAsync();
            return Resultat.Echec<UtilisateurConnecte>(message);
        }

        utilisateur.EchecsPin = 0;
        utilisateur.BloqueJusqua = null;
        await db.SaveChangesAsync();
        return Resultat.Ok(VersConnecte(utilisateur));
    }

    /// <summary>Utilisateur actif (revalidation de la session), ou null s'il a été désactivé.</summary>
    public async Task<UtilisateurConnecte?> ObtenirActif(int id)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var utilisateur = await db.Utilisateurs.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id && u.Actif);
        return utilisateur is null ? null : VersConnecte(utilisateur);
    }

    /// <summary>Prénoms de tous les utilisateurs (affichage des journaux).</summary>
    public async Task<IReadOnlyDictionary<int, string>> Prenoms()
    {
        await using var db = await fabrique.CreateDbContextAsync();
        return await db.Utilisateurs.AsNoTracking().ToDictionaryAsync(u => u.Id, u => u.Prenom);
    }

    public async Task MemoriserApplication(int id, string cleApplication)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        await db.Utilisateurs.Where(u => u.Id == id && u.DerniereApplication != cleApplication)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.DerniereApplication, cleApplication));
    }

    /// <summary>Premier lancement : création de l'administrateur, uniquement si aucun utilisateur n'existe.</summary>
    public async Task<Resultat<int>> CreerPremierAdministrateur(string prenom, string nom, string pin)
    {
        var erreur = Valider(prenom, pin, pinObligatoire: true);
        if (erreur is not null)
        {
            return Resultat.Echec<int>(erreur);
        }

        await using var db = await fabrique.CreateDbContextAsync();
        if (await db.Utilisateurs.AnyAsync())
        {
            return Resultat.Echec<int>("Un administrateur existe déjà.");
        }

        var utilisateur = Nouveau(prenom, nom, Role.Administrateur, pin);
        db.Utilisateurs.Add(utilisateur);
        await db.SaveChangesAsync();
        return Resultat.Ok(utilisateur.Id);
    }

    public async Task<IReadOnlyList<UtilisateurAdmin>> Lister()
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var maintenant = horloge.GetUtcNow().UtcDateTime;
        var tous = await db.Utilisateurs.AsNoTracking().ToListAsync();
        return tous
            .OrderByDescending(u => u.Actif)
            .ThenBy(u => u.Prenom, StringComparer.Create(Format.Culture, true))
            .Select(u => new UtilisateurAdmin(u.Id, u.Prenom, u.Nom, u.Initiales, u.Role, u.Actif, u.BloqueJusqua > maintenant, CouleurAvatar(u.Id)))
            .ToList();
    }

    public async Task<Resultat<int>> Creer(SaisieUtilisateur saisie)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        if (!courant.Peut(Role.Administrateur))
        {
            return Resultat.Echec<int>(Resultat.NonAutorise);
        }

        var erreur = Valider(saisie.Prenom, saisie.Pin, pinObligatoire: true);
        if (erreur is not null)
        {
            return Resultat.Echec<int>(erreur);
        }

        await using var db = await fabrique.CreateDbContextAsync();
        var utilisateur = Nouveau(saisie.Prenom, saisie.Nom, saisie.Role, saisie.Pin!);
        utilisateur.Actif = saisie.Actif;
        db.Utilisateurs.Add(utilisateur);
        await db.SaveChangesAsync();
        return Resultat.Ok(utilisateur.Id);
    }

    public async Task<Resultat> Modifier(int id, SaisieUtilisateur saisie)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        if (!courant.Peut(Role.Administrateur))
        {
            return Resultat.Echec(Resultat.NonAutorise);
        }

        var erreur = Valider(saisie.Prenom, saisie.Pin, pinObligatoire: false);
        if (erreur is not null)
        {
            return Resultat.Echec(erreur);
        }

        await using var db = await fabrique.CreateDbContextAsync();
        var utilisateur = await db.Utilisateurs.FirstOrDefaultAsync(u => u.Id == id);
        if (utilisateur is null)
        {
            return Resultat.Echec("Utilisateur introuvable.");
        }

        var perdAdmin = utilisateur.Role == Role.Administrateur && utilisateur.Actif
            && (saisie.Role != Role.Administrateur || !saisie.Actif);
        if (perdAdmin && !await db.Utilisateurs.AnyAsync(u => u.Id != id && u.Actif && u.Role == Role.Administrateur))
        {
            return Resultat.Echec("Il faut garder au moins un administrateur actif.");
        }

        utilisateur.Prenom = saisie.Prenom.Trim();
        utilisateur.Nom = saisie.Nom?.Trim() ?? string.Empty;
        utilisateur.Initiales = CalculerInitiales(utilisateur.Prenom, utilisateur.Nom);
        utilisateur.Role = saisie.Role;
        utilisateur.Actif = saisie.Actif;
        if (!string.IsNullOrEmpty(saisie.Pin))
        {
            utilisateur.PinHash = Hacheur.HashPassword(utilisateur, saisie.Pin);
            utilisateur.EchecsPin = 0;
            utilisateur.BloqueJusqua = null;
        }

        await db.SaveChangesAsync();
        return Resultat.Ok();
    }

    public async Task<Resultat> Debloquer(int id)
    {
        if (!courant.Peut(Role.Administrateur))
        {
            return Resultat.Echec(Resultat.NonAutorise);
        }

        await using var db = await fabrique.CreateDbContextAsync();
        await db.Utilisateurs.Where(u => u.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.EchecsPin, 0).SetProperty(u => u.BloqueJusqua, (DateTime?)null));
        return Resultat.Ok();
    }

    public static string CalculerInitiales(string prenom, string? nom)
    {
        var p = prenom.Trim();
        var n = nom?.Trim() ?? string.Empty;
        var initiales = n.Length > 0
            ? $"{char.ToUpperInvariant(p.FirstOrDefault())}{char.ToUpperInvariant(n[0])}"
            : p.Length >= 2 ? $"{char.ToUpperInvariant(p[0])}{char.ToLowerInvariant(p[1])}" : p.ToUpperInvariant();
        return initiales.Trim('\0');
    }

    public static bool PinValide(string? pin) => pin is not null && FormatPin().IsMatch(pin);

    private static string? Valider(string? prenom, string? pin, bool pinObligatoire)
    {
        if (string.IsNullOrWhiteSpace(prenom))
        {
            return "Le prénom est obligatoire.";
        }

        if ((pinObligatoire || !string.IsNullOrEmpty(pin)) && !PinValide(pin))
        {
            return "Le code PIN doit comporter 4 à 6 chiffres.";
        }

        return null;
    }

    private static Utilisateur Nouveau(string prenom, string? nom, Role role, string pin)
    {
        var utilisateur = new Utilisateur
        {
            Prenom = prenom.Trim(),
            Nom = nom?.Trim() ?? string.Empty,
            Role = role,
        };
        utilisateur.Initiales = CalculerInitiales(utilisateur.Prenom, utilisateur.Nom);
        utilisateur.PinHash = Hacheur.HashPassword(utilisateur, pin);
        return utilisateur;
    }

    private static UtilisateurConnecte VersConnecte(Utilisateur u) => new(u.Id, u.Prenom, u.Initiales, u.Role, u.DerniereApplication);

    [GeneratedRegex("^[0-9]{4,6}$")]
    private static partial Regex FormatPin();
}
