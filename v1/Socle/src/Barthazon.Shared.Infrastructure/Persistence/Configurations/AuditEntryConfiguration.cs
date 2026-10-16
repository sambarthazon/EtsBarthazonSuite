using Barthazon.Shared.Kernel.Auditing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Barthazon.Shared.Infrastructure.Persistence.Configurations;

internal sealed class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public const string TableName = "sys_audit_entries";

    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable(TableName);
        builder.HasKey(e => e.Id);
        builder.Property(e => e.UserName).HasMaxLength(100);
        builder.Property(e => e.EntityType).HasMaxLength(100).IsRequired();
        builder.Property(e => e.ParentType).HasMaxLength(100);
        builder.Property(e => e.Action).HasMaxLength(50).IsRequired();
        builder.HasIndex(e => e.At);
        builder.HasIndex(e => new { e.EntityType, e.EntityId });
        builder.HasIndex(e => e.ParentId);
    }
}
