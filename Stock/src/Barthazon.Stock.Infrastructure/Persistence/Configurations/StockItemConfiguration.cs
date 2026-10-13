using Barthazon.Shared.Kernel.Text;
using Barthazon.Stock.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Barthazon.Stock.Infrastructure.Persistence.Configurations;

internal sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.ToTable("stk_items");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Code).HasMaxLength(StockItem.CodeMaxLength).IsRequired();
        builder.HasIndex(i => i.Code).IsUnique();
        builder.Property(i => i.Designation).HasMaxLength(StockItem.DesignationMaxLength).IsRequired();
        builder.Property(i => i.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Shape).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Unit).HasConversion<string>().HasMaxLength(20);
        builder.Property(i => i.Dimensions).HasMaxLength(StockItem.DimensionsMaxLength);
        builder.Property(i => i.UsualSupplier).HasMaxLength(StockItem.SupplierMaxLength);
        builder.Property(i => i.Notes).HasMaxLength(StockItem.NotesMaxLength);
        builder.Property(i => i.SearchText).HasMaxLength(SearchNormalizer.MaxSearchTextLength);
        builder.Ignore(i => i.IsCountedInUnits);

        // Liens vers d'autres tables : identifiants seulement (aucune clé étrangère vers le référentiel).
        builder.HasIndex(i => i.MaterialId);
        builder.HasIndex(i => i.OwnerCustomerId);
    }
}
