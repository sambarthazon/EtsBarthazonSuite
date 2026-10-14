using Barthazon.Stock.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Barthazon.Stock.Infrastructure.Persistence.Configurations;

internal sealed class ProductionBatchConfiguration : IEntityTypeConfiguration<ProductionBatch>
{
    public void Configure(EntityTypeBuilder<ProductionBatch> builder)
    {
        builder.ToTable("stk_batches");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Number).HasMaxLength(ProductionBatch.NumberMaxLength).IsRequired();
        builder.HasIndex(b => b.Number).IsUnique();
        builder.HasIndex(b => new { b.Year, b.YearSequence }).IsUnique();
        builder.Property(b => b.PartReference).HasMaxLength(ProductionBatch.ReferenceMaxLength).IsRequired();
        builder.Property(b => b.RevisionIndex).HasMaxLength(ProductionBatch.IndexMaxLength).IsRequired();
        builder.Property(b => b.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.CustomerOrderReference).HasMaxLength(ProductionBatch.OrderReferenceMaxLength);
        builder.Property(b => b.Notes).HasMaxLength(ProductionBatch.NotesMaxLength);
        builder.Ignore(b => b.StepCount);
        builder.HasIndex(b => b.PartId);
        builder.HasIndex(b => b.Status);
        builder.HasMany(b => b.Steps).WithOne().HasForeignKey(s => s.BatchId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(b => b.Steps).HasField("_steps");
    }
}

internal sealed class BatchStepConfiguration : IEntityTypeConfiguration<BatchStep>
{
    public void Configure(EntityTypeBuilder<BatchStep> builder)
    {
        builder.ToTable("stk_batch_steps");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.OperationType).HasConversion<string>().HasMaxLength(30);
        builder.Property(s => s.Description).HasMaxLength(BatchStep.DescriptionMaxLength);
        builder.Property(s => s.Subcontractor).HasMaxLength(BatchStep.SubcontractorMaxLength);
        builder.Ignore(s => s.AuditParentType);
        builder.Ignore(s => s.AuditParentId);
        builder.Ignore(s => s.Definition);
        builder.HasIndex(s => s.MachineId);
    }
}
