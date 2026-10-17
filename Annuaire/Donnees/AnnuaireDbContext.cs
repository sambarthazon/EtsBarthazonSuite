using Barthazon.Commun.Donnees;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Barthazon.Annuaire.Donnees;

/// <summary>Contexte de l'Annuaire : tables ann_, historique de migrations propre.</summary>
public sealed class AnnuaireDbContext(DbContextOptions<AnnuaireDbContext> options) : DbContext(options)
{
    public const string TableHistorique = "__Migrations_Annuaire";

    public DbSet<Client> Clients => Set<Client>();

    public DbSet<Fournisseur> Fournisseurs => Set<Fournisseur>();

    public DbSet<Matiere> Matieres => Set<Matiere>();

    public DbSet<Poste> Postes => Set<Poste>();

    public DbSet<Brut> Bruts => Set<Brut>();

    public DbSet<BrutFournisseur> BrutsFournisseurs => Set<BrutFournisseur>();

    public DbSet<PieceFinie> PiecesFinies => Set<PieceFinie>();

    public DbSet<PieceFinieBrut> PiecesFiniesBruts => Set<PieceFinieBrut>();

    public DbSet<Indice> Indices => Set<Indice>();

    public DbSet<Specification> Specifications => Set<Specification>();

    public DbSet<SpecificationLigne> LignesSpecification => Set<SpecificationLigne>();

    public DbSet<Plan> Plans => Set<Plan>();

    public DbSet<OperationGamme> Operations => Set<OperationGamme>();

    public DbSet<Lien> Liens => Set<Lien>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.Entity<Client>(e =>
        {
            e.ToTable("ann_Client");
            e.Property(c => c.Nom).HasMaxLength(120).IsRequired();
        });

        modelBuilder.Entity<Fournisseur>(e =>
        {
            e.ToTable("ann_Fournisseur");
            e.Property(f => f.Nom).HasMaxLength(120).IsRequired();
        });

        modelBuilder.Entity<Matiere>(e =>
        {
            e.ToTable("ann_Matiere");
            e.Property(m => m.Libelle).HasMaxLength(60).IsRequired();
            e.HasIndex(m => m.Libelle).IsUnique();
            e.HasData(MatieresInitiales.Select((libelle, i) => new Matiere { Id = i + 1, Libelle = libelle }));
        });

        modelBuilder.Entity<Poste>(e =>
        {
            e.ToTable("ann_Poste");
            e.Property(p => p.Libelle).HasMaxLength(60).IsRequired();
            e.HasData(PostesInitiaux.Select((libelle, i) => new Poste { Id = i + 1, Libelle = libelle, Ordre = (i + 1) * 10 }));
        });

        modelBuilder.Entity<Brut>(e =>
        {
            e.ToTable("ann_Brut");
            e.Property(b => b.Reference).HasMaxLength(20).IsRequired();
            e.HasIndex(b => b.Reference).IsUnique();
            e.Property(b => b.DescriptionForme).HasMaxLength(200);
            e.Property(b => b.Designation).HasMaxLength(200);
            e.HasOne(b => b.Matiere).WithMany().HasForeignKey(b => b.MatiereId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(b => b.Fournisseurs).WithOne().HasForeignKey(f => f.BrutId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BrutFournisseur>(e =>
        {
            e.ToTable("ann_BrutFournisseur");
            e.HasKey(f => new { f.BrutId, f.FournisseurId });
            e.Property(f => f.ReferenceFournisseur).HasMaxLength(80);
            e.HasOne(f => f.Fournisseur).WithMany().HasForeignKey(f => f.FournisseurId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<PieceFinie>(e =>
        {
            e.ToTable("ann_PieceFinie");
            e.Property(p => p.Reference).HasMaxLength(80).IsRequired();
            e.Property(p => p.Designation).HasMaxLength(200).IsRequired();
            e.Property(p => p.EmplacementPlanPapier).HasMaxLength(200);
            e.HasIndex(p => new { p.ClientId, p.Reference }).IsUnique();
            e.HasOne(p => p.Client).WithMany().HasForeignKey(p => p.ClientId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(p => p.Bruts).WithOne().HasForeignKey(b => b.PieceFinieId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(p => p.Indices).WithOne().HasForeignKey(i => i.PieceFinieId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<PieceFinieBrut>(e =>
        {
            e.ToTable("ann_PieceFinieBrut");
            e.HasKey(pb => new { pb.PieceFinieId, pb.BrutId });
            e.HasOne(pb => pb.Brut).WithMany().HasForeignKey(pb => pb.BrutId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Indice>(e =>
        {
            e.ToTable("ann_Indice");
            e.Property(i => i.Code).HasMaxLength(10).IsRequired();
            e.Property(i => i.DescriptionChangement).HasMaxLength(500);
            e.HasOne(i => i.Specification).WithOne().HasForeignKey<Specification>(s => s.IndiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(i => i.LignesSpecification).WithOne().HasForeignKey(l => l.IndiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(i => i.Plans).WithOne().HasForeignKey(p => p.IndiceId).OnDelete(DeleteBehavior.Cascade);
            e.HasMany(i => i.Operations).WithOne().HasForeignKey(o => o.IndiceId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Specification>(e => e.ToTable("ann_Specification"));

        modelBuilder.Entity<SpecificationLigne>(e =>
        {
            e.ToTable("ann_SpecificationLigne");
            e.Property(l => l.Libelle).HasMaxLength(100);
            e.Property(l => l.Valeur).HasMaxLength(300);
            e.Property(l => l.Unite).HasMaxLength(20);
        });

        modelBuilder.Entity<Plan>(e =>
        {
            e.ToTable("ann_Plan");
            e.Property(p => p.NomFichier).HasMaxLength(200);
            e.Property(p => p.Chemin).HasMaxLength(400);
            e.Property(p => p.TypeMime).HasMaxLength(100);
        });

        modelBuilder.Entity<OperationGamme>(e =>
        {
            e.ToTable("ann_OperationGamme");
            e.Property(o => o.Description).HasMaxLength(500);
            e.Property(o => o.Remarque).HasMaxLength(300);
            e.HasOne(o => o.Poste).WithMany().HasForeignKey(o => o.PosteId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Lien>(e =>
        {
            e.ToTable("ann_Lien");
            e.Property(l => l.Url).HasMaxLength(500);
            e.Property(l => l.Libelle).HasMaxLength(200);
            e.HasIndex(l => l.PieceFinieId);
            e.HasIndex(l => l.PieceCibleId);
        });
    }

    public static readonly string[] MatieresInitiales = ["S235", "S355", "C45", "42CrMo4", "35NCD16", "304L", "316L", "AU4G (2017)", "7075", "Laiton", "Bronze", "PEHD", "POM"];

    public static readonly string[] PostesInitiaux = ["Tour", "Tour CN", "Fraiseuse", "Centre d'usinage", "Perceuse", "Rectifieuse", "Soudure", "Traitement extérieur", "Contrôle", "Autre"];
}

/// <summary>Pour <c>dotnet ef migrations add</c> uniquement.</summary>
internal sealed class FabriqueConceptionAnnuaire : IDesignTimeDbContextFactory<AnnuaireDbContext>
{
    public AnnuaireDbContext CreateDbContext(string[] args) =>
        new(BaseDeDonneesExtensions.OptionsConception<AnnuaireDbContext>(AnnuaireDbContext.TableHistorique));
}
