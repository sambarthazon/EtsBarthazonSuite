using Barthazon.Catalog.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Barthazon.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class PartConfiguration : IEntityTypeConfiguration<Part>
{
    public void Configure(EntityTypeBuilder<Part> builder)
    {
        builder.ToTable("cat_parts");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.InternalReference).HasMaxLength(50).IsRequired();
        builder.HasIndex(p => p.InternalReference).IsUnique();
        builder.Property(p => p.Designation).HasMaxLength(200).IsRequired();
        builder.Property(p => p.CustomerReference).HasMaxLength(100);
        builder.HasIndex(p => new { p.CustomerId, p.CustomerReference });
        builder.HasIndex(p => p.FamilyId);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Description).HasMaxLength(4000);
        builder.Property(p => p.SearchText).HasMaxLength(4000);
        builder.Ignore(p => p.CurrentRevision);
        builder.Ignore(p => p.InForceRevision);

        // Le client appartient au module Référentiel : simple identifiant, pas de clé étrangère inter-modules.
        builder.HasOne<PartFamily>().WithMany().HasForeignKey(p => p.FamilyId).OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.Revisions).WithOne().HasForeignKey(r => r.PartId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Revisions).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(p => p.Tags).WithOne().HasForeignKey(t => t.PartId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(p => p.Tags).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PartTagConfiguration : IEntityTypeConfiguration<PartTag>
{
    public void Configure(EntityTypeBuilder<PartTag> builder)
    {
        builder.ToTable("cat_part_tags");
        builder.HasKey(t => new { t.PartId, t.TagId });
        builder.HasOne<Tag>().WithMany().HasForeignKey(t => t.TagId).OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PartRevisionConfiguration : IEntityTypeConfiguration<PartRevision>
{
    public void Configure(EntityTypeBuilder<PartRevision> builder)
    {
        builder.ToTable("cat_part_revisions");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Index).HasMaxLength(10).IsRequired();
        builder.HasIndex(r => new { r.PartId, r.Index }).IsUnique();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(r => r.ChangeOrigin).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.BlankShape).HasConversion<string>().HasMaxLength(30);
        builder.Property(r => r.ChangeDescription).HasMaxLength(4000);
        builder.Property(r => r.MaterialNote).HasMaxLength(300);
        builder.Property(r => r.BlankDimensions).HasMaxLength(100);
        foreach (var name in new[] { nameof(PartRevision.GeneralTolerance), nameof(PartRevision.SurfaceRoughness), nameof(PartRevision.HeatTreatment), nameof(PartRevision.SurfaceTreatment), nameof(PartRevision.Hardness) })
        {
            builder.Property(name).HasMaxLength(200);
        }

        foreach (var name in new[] { nameof(PartRevision.Tooling), nameof(PartRevision.WorkshopNotes), nameof(PartRevision.InspectionNotes) })
        {
            builder.Property(name).HasMaxLength(4000);
        }

        foreach (var name in new[] { nameof(PartRevision.LengthMm), nameof(PartRevision.WidthMm), nameof(PartRevision.HeightMm), nameof(PartRevision.DiameterMm), nameof(PartRevision.MassKg) })
        {
            builder.Property(name).HasPrecision(12, 3);
        }

        builder.HasIndex(r => r.MaterialId);
        builder.Ignore(r => r.AuditParentType);
        builder.Ignore(r => r.AuditParentId);
        builder.HasMany(r => r.Characteristics).WithOne().HasForeignKey(c => c.RevisionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Characteristics).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(r => r.Operations).WithOne().HasForeignKey(o => o.RevisionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Operations).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasMany(r => r.Documents).WithOne().HasForeignKey(d => d.RevisionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Documents).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class PartCharacteristicConfiguration : IEntityTypeConfiguration<PartCharacteristic>
{
    public void Configure(EntityTypeBuilder<PartCharacteristic> builder)
    {
        builder.ToTable("cat_part_characteristics");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(100).IsRequired();
        builder.Property(c => c.Value).HasMaxLength(500);
        builder.Property(c => c.Unit).HasMaxLength(30);
        builder.Ignore(c => c.AuditParentType);
        builder.Ignore(c => c.AuditParentId);
    }
}

internal sealed class RoutingOperationConfiguration : IEntityTypeConfiguration<RoutingOperation>
{
    public void Configure(EntityTypeBuilder<RoutingOperation> builder)
    {
        builder.ToTable("cat_routing_operations");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(o => o.Description).HasMaxLength(4000);
        builder.Property(o => o.NcProgramReference).HasMaxLength(100);
        builder.Property(o => o.Tooling).HasMaxLength(1000);
        builder.Property(o => o.Subcontractor).HasMaxLength(200);
        builder.Property(o => o.SetupTimeMin).HasPrecision(10, 2);
        builder.Property(o => o.CycleTimeMin).HasPrecision(10, 2);
        builder.HasIndex(o => o.MachineId);
        builder.Ignore(o => o.AuditParentType);
        builder.Ignore(o => o.AuditParentId);
    }
}

internal sealed class PartDocumentConfiguration : IEntityTypeConfiguration<PartDocument>
{
    public void Configure(EntityTypeBuilder<PartDocument> builder)
    {
        builder.ToTable("cat_documents");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(10);
        builder.Property(d => d.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(d => d.Title).HasMaxLength(200).IsRequired();
        builder.Property(d => d.FileName).HasMaxLength(260);
        builder.Property(d => d.ContentType).HasMaxLength(100);
        builder.Property(d => d.Sha256).HasMaxLength(64);
        builder.Property(d => d.StorageKey).HasMaxLength(500);
        builder.Property(d => d.PaperLocation).HasMaxLength(300);
        builder.Property(d => d.Notes).HasMaxLength(2000);
        builder.HasIndex(d => d.PartId);
        builder.HasIndex(d => d.StorageKey);
        builder.Ignore(d => d.AuditParentType);
        builder.Ignore(d => d.AuditParentId);
    }
}

internal sealed class PartLinkConfiguration : IEntityTypeConfiguration<PartLink>
{
    public void Configure(EntityTypeBuilder<PartLink> builder)
    {
        builder.ToTable("cat_part_links");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Quantity).HasPrecision(12, 3);
        builder.Property(l => l.Note).HasMaxLength(500);
        builder.HasIndex(l => new { l.SourcePartId, l.TargetPartId, l.Type }).IsUnique();
        builder.HasIndex(l => l.TargetPartId);
        builder.HasOne<Part>().WithMany().HasForeignKey(l => l.SourcePartId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Part>().WithMany().HasForeignKey(l => l.TargetPartId).OnDelete(DeleteBehavior.Cascade);
        builder.Ignore(l => l.AuditParentType);
        builder.Ignore(l => l.AuditParentId);
    }
}

internal sealed class FavoriteConfiguration : IEntityTypeConfiguration<Favorite>
{
    public void Configure(EntityTypeBuilder<Favorite> builder)
    {
        builder.ToTable("cat_favorites");
        builder.HasKey(f => new { f.UserId, f.PartId });
        builder.HasOne<Part>().WithMany().HasForeignKey(f => f.PartId).OnDelete(DeleteBehavior.Cascade);
    }
}
