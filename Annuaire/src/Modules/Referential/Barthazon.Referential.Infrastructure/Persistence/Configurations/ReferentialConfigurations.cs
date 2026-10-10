using Barthazon.Referential.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Barthazon.Referential.Infrastructure.Persistence.Configurations;

internal sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("ref_customers");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Code).HasMaxLength(20).IsRequired();
        builder.HasIndex(c => c.Code).IsUnique();
        builder.Property(c => c.Name).HasMaxLength(200).IsRequired();
        builder.Property(c => c.Address).HasMaxLength(300);
        builder.Property(c => c.PostalCode).HasMaxLength(20);
        builder.Property(c => c.City).HasMaxLength(100);
        builder.Property(c => c.Notes).HasMaxLength(4000);
        builder.HasMany(c => c.Contacts).WithOne().HasForeignKey(c => c.CustomerId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(c => c.Contacts).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class CustomerContactConfiguration : IEntityTypeConfiguration<CustomerContact>
{
    public void Configure(EntityTypeBuilder<CustomerContact> builder)
    {
        builder.ToTable("ref_customer_contacts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(150).IsRequired();
        builder.Property(c => c.Function).HasMaxLength(100);
        builder.Property(c => c.Phone).HasMaxLength(50);
        builder.Property(c => c.Email).HasMaxLength(200);
        builder.Property(c => c.Notes).HasMaxLength(2000);
    }
}

internal sealed class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.ToTable("ref_materials");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Designation).HasMaxLength(150).IsRequired();
        builder.Property(m => m.Grade).HasMaxLength(100);
        builder.Property(m => m.Family).HasConversion<string>().HasMaxLength(30);
        builder.Property(m => m.DensityKgDm3).HasPrecision(8, 3);
        builder.Property(m => m.Notes).HasMaxLength(2000);
        builder.HasIndex(m => new { m.Designation, m.Grade });
    }
}

internal sealed class MachineConfiguration : IEntityTypeConfiguration<Machine>
{
    public void Configure(EntityTypeBuilder<Machine> builder)
    {
        builder.ToTable("ref_machines");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).HasMaxLength(100).IsRequired();
        builder.HasIndex(m => m.Name).IsUnique();
        builder.Property(m => m.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(m => m.Notes).HasMaxLength(2000);
    }
}

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("ref_users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.UserName).HasMaxLength(50).IsRequired();
        builder.HasIndex(u => u.UserName).IsUnique();
        builder.Property(u => u.DisplayName).HasMaxLength(100).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
    }
}
