using Barthazon.Commun.Contrats;
using Barthazon.Commun.Donnees;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Barthazon.Stock.Donnees;

/// <summary>Quantité courante d'un brut ou d'une pièce finie, tenue à jour avec chaque mouvement.</summary>
public sealed class NiveauStock
{
    public int Id { get; set; }

    public TypeReference TypeReference { get; set; }

    /// <summary>Identifiant du brut ou de la pièce finie dans l'Annuaire (sans contrainte SQL entre applications).</summary>
    public int ReferenceId { get; set; }

    public decimal Quantite { get; set; }

    public decimal? SeuilAlerte { get; set; }

    /// <summary>Date du dernier mouvement ; null tant que la référence n'a jamais été mouvementée.</summary>
    public DateTime? DernierMouvement { get; set; }
}

public enum TypeMouvement
{
    Arrivage = 1,
    Fabrication = 2,
    Sortie = 3,
    Ajustement = 4,
    Annulation = 5,
}

public enum MotifSortie
{
    LivraisonClient = 1,
    Rebut = 2,
    Autre = 3,
    HorsFabrication = 4,
    RetourFournisseur = 5,
}

/// <summary>Un changement de quantité. Jamais supprimé : seulement annulé par un mouvement inverse.</summary>
public sealed class Mouvement
{
    public int Id { get; set; }

    /// <summary>Opération : une fabrication crée deux mouvements (brut −, pièce +) qui s'annulent ensemble.</summary>
    public Guid OperationId { get; set; }

    /// <summary>Date du mouvement (antidatable, jamais dans le futur).</summary>
    public DateTime Date { get; set; }

    /// <summary>Date de saisie réelle.</summary>
    public DateTime DateSaisie { get; set; }

    public int UtilisateurId { get; set; }

    public TypeReference TypeReference { get; set; }

    public int ReferenceId { get; set; }

    public TypeMouvement TypeMouvement { get; set; }

    public MotifSortie? Motif { get; set; }

    /// <summary>Quantité signée : + entrée, − sortie.</summary>
    public decimal Quantite { get; set; }

    public decimal QuantiteApres { get; set; }

    public int? FournisseurId { get; set; }

    public int? ClientId { get; set; }

    public string? NumeroBL { get; set; }

    public string? Commentaire { get; set; }

    /// <summary>Mouvement d'annulation qui a annulé celui-ci.</summary>
    public int? AnnuleParId { get; set; }

    /// <summary>Pour une annulation : mouvement annulé.</summary>
    public int? AnnuleId { get; set; }
}

/// <summary>Contexte du Stock : tables stk_, historique de migrations propre.</summary>
public sealed class StockDbContext(DbContextOptions<StockDbContext> options) : DbContext(options)
{
    public const string TableHistorique = "__Migrations_Stock";

    public DbSet<NiveauStock> Niveaux => Set<NiveauStock>();

    public DbSet<Mouvement> Mouvements => Set<Mouvement>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<NiveauStock>(e =>
        {
            e.ToTable("stk_NiveauStock");
            e.HasIndex(n => new { n.TypeReference, n.ReferenceId }).IsUnique();
        });

        modelBuilder.Entity<Mouvement>(e =>
        {
            e.ToTable("stk_Mouvement");
            e.Property(m => m.NumeroBL).HasMaxLength(60);
            e.Property(m => m.Commentaire).HasMaxLength(500);
            e.HasIndex(m => new { m.TypeReference, m.ReferenceId });
            e.HasIndex(m => m.OperationId);
            e.HasIndex(m => m.Date);
        });
    }
}

/// <summary>Pour <c>dotnet ef migrations add</c> uniquement.</summary>
internal sealed class FabriqueConceptionStock : IDesignTimeDbContextFactory<StockDbContext>
{
    public StockDbContext CreateDbContext(string[] args) =>
        new(BaseDeDonneesExtensions.OptionsConception<StockDbContext>(StockDbContext.TableHistorique));
}
