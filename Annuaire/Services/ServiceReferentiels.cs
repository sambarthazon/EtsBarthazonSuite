using Barthazon.Annuaire.Donnees;
using Barthazon.Commun.Contrats;
using Barthazon.Commun.Historique;
using Barthazon.Commun.Outils;
using Barthazon.Commun.Securite;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Annuaire.Services;

public enum SorteTiers
{
    Client,
    Fournisseur,
}

/// <summary>Un contact d'un client ou d'un fournisseur (tous les champs sont facultatifs, mais pas tous vides).</summary>
public sealed record ContactTiers(string? Nom, string? Telephone, string? Email);

/// <summary>Ligne de la liste des clients ou des fournisseurs.</summary>
/// <param name="Contacts">Dans l'ordre de saisie.</param>
/// <param name="Utilisations">Pièces finies du client, ou bruts du fournisseur.</param>
public sealed record LigneTiers(int Id, string Nom, IReadOnlyList<ContactTiers> Contacts, string? Remarques, bool Archive, int Utilisations)
{
    /// <summary>Premier contact (affiché dans les listes), ou null.</summary>
    public ContactTiers? PremierContact => Contacts.Count > 0 ? Contacts[0] : null;

    /// <summary>Noms, téléphones et e-mails de tous les contacts, pour la recherche.</summary>
    public string TexteContacts => string.Join(' ', Contacts.Select(c => $"{c.Nom} {c.Telephone} {c.Email}"));
}

/// <summary>Contact en cours de saisie (« + » ajoute une ligne, « × » la retire).</summary>
public sealed class SaisieContact
{
    public string? Nom { get; set; }

    public string? Telephone { get; set; }

    public string? Email { get; set; }

    public static SaisieContact Depuis(ContactTiers c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new() { Nom = c.Nom, Telephone = c.Telephone, Email = c.Email };
    }
}

/// <param name="Contacts">Les lignes entièrement vides sont ignorées.</param>
public sealed record SaisieTiers(string Nom, string? Remarques = null, IReadOnlyList<SaisieContact>? Contacts = null);

public sealed record LigneListe(int Id, string Libelle, int Utilisations);

/// <summary>Clients, fournisseurs, matières et postes de gamme.</summary>
public sealed class ServiceReferentiels(IDbContextFactory<AnnuaireDbContext> fabrique, IUtilisateurCourant courant, ServiceHistorique historique)
{
    private static readonly StringComparer Tri = StringComparer.Create(Format.Culture, true);

    public async Task<IReadOnlyList<LigneTiers>> ListerTiers(SorteTiers sorte, bool inclureArchives = false)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        List<LigneTiers> lignes;
        if (sorte == SorteTiers.Client)
        {
            var usages = await db.PiecesFinies.GroupBy(p => p.ClientId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
            lignes = (await db.Clients.AsNoTracking().Include(c => c.Contacts).Where(c => inclureArchives || !c.Archive).ToListAsync())
                .Select(c => new LigneTiers(c.Id, c.Nom, Contacts(c.Contacts), c.Remarques, c.Archive, usages.GetValueOrDefault(c.Id)))
                .ToList();
        }
        else
        {
            var usages = await db.BrutsFournisseurs.GroupBy(b => b.FournisseurId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
            lignes = (await db.Fournisseurs.AsNoTracking().Include(f => f.Contacts).Where(f => inclureArchives || !f.Archive).ToListAsync())
                .Select(f => new LigneTiers(f.Id, f.Nom, Contacts(f.Contacts), f.Remarques, f.Archive, usages.GetValueOrDefault(f.Id)))
                .ToList();
        }

        return lignes.OrderBy(l => l.Nom, Tri).ToList();
    }

    private static List<ContactTiers> Contacts(IEnumerable<Contact> contacts) =>
        contacts.OrderBy(c => c.Ordre).ThenBy(c => c.Id).Select(c => new ContactTiers(c.Nom, c.Telephone, c.Email)).ToList();

    public async Task<LigneTiers?> ObtenirTiers(SorteTiers sorte, int id) =>
        (await ListerTiers(sorte, inclureArchives: true)).FirstOrDefault(t => t.Id == id);

    /// <summary>Crée (id null) ou modifie un client ou un fournisseur.</summary>
    public async Task<Resultat<int>> EnregistrerTiers(SorteTiers sorte, int? id, SaisieTiers saisie)
    {
        ArgumentNullException.ThrowIfNull(saisie);
        if (!courant.Peut(Role.Gestion))
        {
            return Resultat.Echec<int>(Resultat.NonAutorise);
        }

        var nom = saisie.Nom?.Trim() ?? string.Empty;
        if (nom.Length == 0)
        {
            return Resultat.Echec<int>("Le nom est obligatoire.");
        }

        var contacts = (saisie.Contacts ?? [])
            .Select(c => new { Nom = Net(c.Nom), Telephone = Net(c.Telephone), Email = Net(c.Email) })
            .Where(c => c.Nom is not null || c.Telephone is not null || c.Email is not null)
            .Select((c, i) => new Contact { Nom = c.Nom, Telephone = c.Telephone, Email = c.Email, Ordre = i })
            .ToList();
        if (contacts.FirstOrDefault(c => c.Email is not null && !EmailPlausible(c.Email)) is { } faux)
        {
            return Resultat.Echec<int>($"L'e-mail « {faux.Email} » ne semble pas valide.");
        }

        await using var db = await fabrique.CreateDbContextAsync();
        var libelle = sorte == SorteTiers.Client ? "le client" : "le fournisseur";
        var doublon = sorte == SorteTiers.Client
            ? await db.Clients.AnyAsync(c => c.Id != id && c.Nom.ToLower() == nom.ToLower())
            : await db.Fournisseurs.AnyAsync(f => f.Id != id && f.Nom.ToLower() == nom.ToLower());
        if (doublon)
        {
            return Resultat.Echec<int>($"Il existe déjà {libelle} « {nom} ».");
        }

        int idFinal;
        if (sorte == SorteTiers.Client)
        {
            var client = id is null ? new Client() : await db.Clients.Include(c => c.Contacts).FirstOrDefaultAsync(c => c.Id == id.Value);
            if (client is null)
            {
                return Resultat.Echec<int>("Client introuvable.");
            }

            (client.Nom, client.Remarques) = (nom, Net(saisie.Remarques));
            db.Contacts.RemoveRange(client.Contacts);
            client.Contacts = contacts;
            if (id is null)
            {
                db.Clients.Add(client);
            }

            await db.SaveChangesAsync();
            idFinal = client.Id;
        }
        else
        {
            var fournisseur = id is null ? new Fournisseur() : await db.Fournisseurs.Include(f => f.Contacts).FirstOrDefaultAsync(f => f.Id == id.Value);
            if (fournisseur is null)
            {
                return Resultat.Echec<int>("Fournisseur introuvable.");
            }

            (fournisseur.Nom, fournisseur.Remarques) = (nom, Net(saisie.Remarques));
            db.Contacts.RemoveRange(fournisseur.Contacts);
            fournisseur.Contacts = contacts;
            if (id is null)
            {
                db.Fournisseurs.Add(fournisseur);
            }

            await db.SaveChangesAsync();
            idFinal = fournisseur.Id;
        }

        var type = sorte == SorteTiers.Client ? TypesObjet.Client : TypesObjet.Fournisseur;
        await historique.Ecrire(AnnuaireExtensions.Cle, type, idFinal, id is null ? "creation" : "modification",
            $"{historique.Sujet} a {(id is null ? "créé" : "modifié")} {libelle} {nom}");
        return Resultat.Ok(idFinal);
    }

    public async Task<Resultat> ArchiverTiers(SorteTiers sorte, int id, bool archive)
    {
        if (!courant.Peut(Role.Gestion))
        {
            return Resultat.Echec(Resultat.NonAutorise);
        }

        await using var db = await fabrique.CreateDbContextAsync();
        var n = sorte == SorteTiers.Client
            ? await db.Clients.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.Archive, archive))
            : await db.Fournisseurs.Where(f => f.Id == id).ExecuteUpdateAsync(s => s.SetProperty(f => f.Archive, archive));
        return n == 0 ? Resultat.Echec("Introuvable.") : Resultat.Ok();
    }

    public async Task<IReadOnlyList<LigneListe>> ListerMatieres()
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var usages = await db.Bruts.GroupBy(b => b.MatiereId).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        return (await db.Matieres.AsNoTracking().ToListAsync())
            .Select(m => new LigneListe(m.Id, m.Libelle, usages.GetValueOrDefault(m.Id)))
            .OrderBy(m => m.Libelle, Tri)
            .ToList();
    }

    public async Task<IReadOnlyList<LigneListe>> ListerPostes()
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var usages = await db.Operations.Where(o => o.PosteId != null).GroupBy(o => o.PosteId!.Value).Select(g => new { g.Key, N = g.Count() }).ToDictionaryAsync(x => x.Key, x => x.N);
        return (await db.Postes.AsNoTracking().OrderBy(p => p.Ordre).ThenBy(p => p.Id).ToListAsync())
            .Select(p => new LigneListe(p.Id, p.Libelle, usages.GetValueOrDefault(p.Id)))
            .ToList();
    }

    /// <summary>Ajout d'une matière (à la volée depuis un brut : rôle Gestion).</summary>
    public async Task<Resultat<int>> CreerMatiere(string libelle)
    {
        if (!courant.Peut(Role.Gestion))
        {
            return Resultat.Echec<int>(Resultat.NonAutorise);
        }

        var texte = libelle?.Trim() ?? string.Empty;
        if (texte.Length == 0)
        {
            return Resultat.Echec<int>("Le libellé est obligatoire.");
        }

        await using var db = await fabrique.CreateDbContextAsync();
        var existante = await db.Matieres.FirstOrDefaultAsync(m => m.Libelle.ToLower() == texte.ToLower());
        if (existante is not null)
        {
            return Resultat.Ok(existante.Id);
        }

        var matiere = new Matiere { Libelle = texte };
        db.Matieres.Add(matiere);
        await db.SaveChangesAsync();
        return Resultat.Ok(matiere.Id);
    }

    public async Task<Resultat<int>> CreerPoste(string libelle)
    {
        if (!courant.Peut(Role.Administrateur))
        {
            return Resultat.Echec<int>(Resultat.NonAutorise);
        }

        var texte = libelle?.Trim() ?? string.Empty;
        if (texte.Length == 0)
        {
            return Resultat.Echec<int>("Le libellé est obligatoire.");
        }

        await using var db = await fabrique.CreateDbContextAsync();
        var ordre = (await db.Postes.MaxAsync(p => (int?)p.Ordre) ?? 0) + 10;
        var poste = new Poste { Libelle = texte, Ordre = ordre };
        db.Postes.Add(poste);
        await db.SaveChangesAsync();
        return Resultat.Ok(poste.Id);
    }

    public async Task<Resultat> Renommer(bool poste, int id, string libelle)
    {
        if (!courant.Peut(Role.Administrateur))
        {
            return Resultat.Echec(Resultat.NonAutorise);
        }

        var texte = libelle?.Trim() ?? string.Empty;
        if (texte.Length == 0)
        {
            return Resultat.Echec("Le libellé est obligatoire.");
        }

        await using var db = await fabrique.CreateDbContextAsync();
        if (poste)
        {
            await db.Postes.Where(p => p.Id == id).ExecuteUpdateAsync(s => s.SetProperty(p => p.Libelle, texte));
        }
        else
        {
            if (await db.Matieres.AnyAsync(m => m.Id != id && m.Libelle.ToLower() == texte.ToLower()))
            {
                return Resultat.Echec($"La matière « {texte} » existe déjà.");
            }

            await db.Matieres.Where(m => m.Id == id).ExecuteUpdateAsync(s => s.SetProperty(m => m.Libelle, texte));
        }

        return Resultat.Ok();
    }

    /// <summary>Suppression d'une matière ou d'un poste, seulement s'il n'est pas utilisé.</summary>
    public async Task<Resultat> Supprimer(bool poste, int id)
    {
        if (!courant.Peut(Role.Administrateur))
        {
            return Resultat.Echec(Resultat.NonAutorise);
        }

        await using var db = await fabrique.CreateDbContextAsync();
        if (poste)
        {
            if (await db.Operations.AnyAsync(o => o.PosteId == id))
            {
                return Resultat.Echec("Ce poste est utilisé dans des gammes : renommez-le plutôt.");
            }

            await db.Postes.Where(p => p.Id == id).ExecuteDeleteAsync();
        }
        else
        {
            if (await db.Bruts.AnyAsync(b => b.MatiereId == id))
            {
                return Resultat.Echec("Cette matière est utilisée par des bruts : renommez-la plutôt.");
            }

            await db.Matieres.Where(m => m.Id == id).ExecuteDeleteAsync();
        }

        return Resultat.Ok();
    }

    /// <summary>Contrôle volontairement simple : quelque chose, une arobase, un point après.</summary>
    private static bool EmailPlausible(string email)
    {
        var arobase = email.IndexOf('@', StringComparison.Ordinal);
        return arobase > 0 && email.LastIndexOf('.') > arobase + 1 && !email.EndsWith('.') && !email.Contains(' ', StringComparison.Ordinal);
    }

    internal static string? Net(string? texte) => string.IsNullOrWhiteSpace(texte) ? null : texte.Trim();
}
