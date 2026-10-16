using Barthazon.Commun.Securite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Barthazon.Commun.Donnees;

public sealed class Utilisateur
{
    public int Id { get; set; }

    public string Prenom { get; set; } = string.Empty;

    public string Nom { get; set; } = string.Empty;

    public string Initiales { get; set; } = string.Empty;

    public string PinHash { get; set; } = string.Empty;

    public Role Role { get; set; } = Role.Consultation;

    public bool Actif { get; set; } = true;

    /// <summary>Clé de la dernière application utilisée (ouverte directement après connexion).</summary>
    public string? DerniereApplication { get; set; }

    public int EchecsPin { get; set; }

    public DateTime? BloqueJusqua { get; set; }

    public string NomComplet => string.IsNullOrWhiteSpace(Nom) ? Prenom : $"{Prenom} {Nom}";
}

/// <summary>Une ligne du journal des modifications, avec une phrase lisible.</summary>
public sealed class EntreeHistorique
{
    public int Id { get; set; }

    public DateTime Date { get; set; }

    public int? UtilisateurId { get; set; }

    public string Module { get; set; } = string.Empty;

    public string TypeObjet { get; set; } = string.Empty;

    public int ObjetId { get; set; }

    public string Action { get; set; } = string.Empty;

    public string Phrase { get; set; } = string.Empty;
}

public sealed class Parametre
{
    public string Cle { get; set; } = string.Empty;

    public string Valeur { get; set; } = string.Empty;
}

/// <summary>Contexte du socle : utilisateurs, historique, paramètres (tables com_).</summary>
public sealed class CommunDbContext(DbContextOptions<CommunDbContext> options) : DbContext(options)
{
    public const string TableHistorique = "__Migrations_Commun";

    public DbSet<Utilisateur> Utilisateurs => Set<Utilisateur>();

    public DbSet<EntreeHistorique> Historique => Set<EntreeHistorique>();

    public DbSet<Parametre> Parametres => Set<Parametre>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Utilisateur>(e =>
        {
            e.ToTable("com_Utilisateur");
            e.Property(u => u.Prenom).HasMaxLength(60).IsRequired();
            e.Property(u => u.Nom).HasMaxLength(60);
            e.Property(u => u.Initiales).HasMaxLength(3);
            e.Property(u => u.PinHash).IsRequired();
            e.Property(u => u.DerniereApplication).HasMaxLength(40);
            e.Ignore(u => u.NomComplet);
        });

        modelBuilder.Entity<EntreeHistorique>(e =>
        {
            e.ToTable("com_Historique");
            e.Property(h => h.Module).HasMaxLength(40);
            e.Property(h => h.TypeObjet).HasMaxLength(40);
            e.Property(h => h.Action).HasMaxLength(40);
            e.Property(h => h.Phrase).HasMaxLength(500);
            e.HasIndex(h => new { h.TypeObjet, h.ObjetId });
            e.HasIndex(h => h.Date);
        });

        modelBuilder.Entity<Parametre>(e =>
        {
            e.ToTable("com_Parametre");
            e.HasKey(p => p.Cle);
            e.Property(p => p.Cle).HasMaxLength(80);
        });
    }
}

/// <summary>Pour <c>dotnet ef migrations add</c> uniquement.</summary>
internal sealed class FabriqueConceptionCommun : IDesignTimeDbContextFactory<CommunDbContext>
{
    public CommunDbContext CreateDbContext(string[] args) =>
        new(BaseDeDonneesExtensions.OptionsConception<CommunDbContext>(CommunDbContext.TableHistorique));
}
