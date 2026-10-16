using Barthazon.Stock.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Barthazon.Stock.Infrastructure.Persistence.Configurations;

internal sealed class InventoryCountConfiguration : IEntityTypeConfiguration<InventoryCount>
{
    public void Configure(EntityTypeBuilder<InventoryCount> builder)
    {
        builder.ToTable("stk_inventories");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Number).HasMaxLength(InventoryCount.NumberMaxLength).IsRequired();
        builder.HasIndex(i => i.Number).IsUnique();
        builder.HasIndex(i => new { i.Year, i.YearSequence }).IsUnique();
        builder.Property(i => i.Title).HasMaxLength(InventoryCount.TitleMaxLength).IsRequired();
        builder.Property(i => i.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.LocationScope).HasMaxLength(4000).IsRequired();
        builder.Property(i => i.ClosedByName).HasMaxLength(StockTransaction.UserNameMaxLength);
        builder.Ignore(i => i.LocationIds);
        builder.Ignore(i => i.IsOpen);
        builder.HasIndex(i => i.Status);
        builder.HasMany(i => i.Lines).WithOne().HasForeignKey(l => l.InventoryId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.Lines).HasField("_lines");
    }
}

internal sealed class InventoryCountLineConfiguration : IEntityTypeConfiguration<InventoryCountLine>
{
    public void Configure(EntityTypeBuilder<InventoryCountLine> builder)
    {
        builder.ToTable("stk_inventory_lines");
        builder.HasKey(l => l.Id);

        // Clé fixée à la création : une ligne ajoutée à un inventaire suivi est bien une insertion.
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.SubjectKind).HasConversion<string>().HasMaxLength(10);
        builder.Property(l => l.Bucket).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Lot).HasMaxLength(StockKey.LotMaxLength).IsRequired();
        builder.Property(l => l.CountedByName).HasMaxLength(StockTransaction.UserNameMaxLength);
        builder.Ignore(l => l.Key);
        builder.Ignore(l => l.Difference);
        builder.HasIndex(l => new { l.InventoryId, l.SubjectKind, l.ItemId, l.PartId, l.RevisionId, l.Bucket, l.BatchId, l.Stage, l.LocationId, l.Lot })
            .IsUnique().HasDatabaseName("UX_stk_inventory_lines_key");
    }
}
