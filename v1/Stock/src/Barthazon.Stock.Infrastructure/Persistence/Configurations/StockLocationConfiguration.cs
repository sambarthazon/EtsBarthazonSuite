using Barthazon.Stock.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Barthazon.Stock.Infrastructure.Persistence.Configurations;

internal sealed class StockLocationConfiguration : IEntityTypeConfiguration<StockLocation>
{
    public void Configure(EntityTypeBuilder<StockLocation> builder)
    {
        builder.ToTable("stk_locations");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Code).HasMaxLength(StockLocation.CodeMaxLength).IsRequired();
        builder.HasIndex(l => l.Code).IsUnique();
        builder.Property(l => l.Name).HasMaxLength(StockLocation.NameMaxLength).IsRequired();
        builder.Property(l => l.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Description).HasMaxLength(StockLocation.DescriptionMaxLength);
    }
}
