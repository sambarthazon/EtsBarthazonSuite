using Barthazon.Catalog.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Barthazon.Catalog.Infrastructure.Persistence.Configurations;

internal sealed class PartFamilyConfiguration : IEntityTypeConfiguration<PartFamily>
{
    public void Configure(EntityTypeBuilder<PartFamily> builder)
    {
        builder.ToTable("cat_part_families");
        builder.HasKey(f => f.Id);
        builder.Property(f => f.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(f => f.Name).IsUnique();
        builder.Property(f => f.Description).HasMaxLength(1000);
    }
}

internal sealed class TagConfiguration : IEntityTypeConfiguration<Tag>
{
    public void Configure(EntityTypeBuilder<Tag> builder)
    {
        builder.ToTable("cat_tags");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Label).HasMaxLength(50).IsRequired();
        builder.HasIndex(t => t.Label).IsUnique();
        builder.Property(t => t.Color).HasConversion<string>().HasMaxLength(20);
    }
}
