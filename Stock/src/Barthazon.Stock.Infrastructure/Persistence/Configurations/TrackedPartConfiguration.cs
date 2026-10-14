using Barthazon.Stock.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Barthazon.Stock.Infrastructure.Persistence.Configurations;

internal sealed class TrackedPartConfiguration : IEntityTypeConfiguration<TrackedPart>
{
    public void Configure(EntityTypeBuilder<TrackedPart> builder)
    {
        builder.ToTable("stk_tracked_parts");
        builder.HasKey(t => t.Id);

        // Identifiant de la pièce de l'annuaire : aucune clé étrangère entre applications.
        builder.HasIndex(t => t.PartId).IsUnique();
        builder.Property(t => t.LastKnownReference).HasMaxLength(TrackedPart.ReferenceMaxLength).IsRequired();
        builder.Property(t => t.LastKnownDesignation).HasMaxLength(TrackedPart.DesignationMaxLength).IsRequired();
        builder.Property(t => t.Notes).HasMaxLength(TrackedPart.NotesMaxLength);
        builder.HasMany(t => t.Sources).WithOne().HasForeignKey(s => s.TrackedPartId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Sources).HasField("_sources");
    }
}

internal sealed class BlankSourceConfiguration : IEntityTypeConfiguration<BlankSource>
{
    public void Configure(EntityTypeBuilder<BlankSource> builder)
    {
        builder.ToTable("stk_blank_sources");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Kind).HasConversion<string>().HasMaxLength(10);
        builder.Property(s => s.Note).HasMaxLength(BlankSource.NoteMaxLength);
        builder.Ignore(s => s.AuditParentType);
        builder.Ignore(s => s.AuditParentId);
        builder.HasIndex(s => s.ItemId);
        builder.HasIndex(s => s.SourcePartId);
    }
}
