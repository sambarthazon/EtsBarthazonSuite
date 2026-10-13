using Barthazon.Stock.Core.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Barthazon.Stock.Infrastructure.Persistence.Configurations;

internal sealed class StockTransactionConfiguration : IEntityTypeConfiguration<StockTransaction>
{
    public void Configure(EntityTypeBuilder<StockTransaction> builder)
    {
        builder.ToTable("stk_transactions");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).ValueGeneratedNever();
        builder.HasIndex(t => t.Sequence).IsUnique();
        builder.Property(t => t.Type).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.Reason).HasConversion<string>().HasMaxLength(30);
        builder.Property(t => t.RecordedByName).HasMaxLength(StockTransaction.UserNameMaxLength);
        builder.Property(t => t.Reference).HasMaxLength(StockTransaction.ReferenceMaxLength);
        builder.Property(t => t.Comment).HasMaxLength(StockTransaction.CommentMaxLength);

        // Seul champ modifiable après création : deux annulations simultanées du même mouvement sont impossibles.
        builder.Property(t => t.ReversedByTransactionId).IsConcurrencyToken();
        builder.HasIndex(t => t.OccurredAt);
        builder.HasIndex(t => t.BatchId);
        builder.Ignore(t => t.Number);
        builder.Ignore(t => t.IsReversed);
        builder.HasMany(t => t.Lines).WithOne().HasForeignKey(l => l.TransactionId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(t => t.Lines).HasField("_lines");
    }
}

internal sealed class StockTransactionLineConfiguration : IEntityTypeConfiguration<StockTransactionLine>
{
    public void Configure(EntityTypeBuilder<StockTransactionLine> builder)
    {
        builder.ToTable("stk_transaction_lines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();
        builder.Property(l => l.SubjectKind).HasConversion<string>().HasMaxLength(10);
        builder.Property(l => l.Bucket).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Lot).HasMaxLength(StockKey.LotMaxLength);
        builder.Ignore(l => l.Key);
        builder.HasIndex(l => l.ItemId);
        builder.HasIndex(l => l.PartId);
        builder.HasIndex(l => l.BatchId);
        builder.HasIndex(l => l.LocationId);
    }
}

internal sealed class StockBalanceConfiguration : IEntityTypeConfiguration<StockBalance>
{
    public void Configure(EntityTypeBuilder<StockBalance> builder)
    {
        builder.ToTable("stk_balances");
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).ValueGeneratedNever();
        builder.Property(b => b.SubjectKind).HasConversion<string>().HasMaxLength(10);
        builder.Property(b => b.Bucket).HasConversion<string>().HasMaxLength(20);
        builder.Property(b => b.Lot).HasMaxLength(StockKey.LotMaxLength).IsRequired();
        builder.Property(b => b.Version).IsConcurrencyToken();
        builder.Ignore(b => b.Key);

        // Clé de la case : aucune colonne nulle (valeurs neutres), même comportement sous SQLite et PostgreSQL.
        builder.HasIndex(b => new { b.SubjectKind, b.ItemId, b.PartId, b.RevisionId, b.Bucket, b.BatchId, b.Stage, b.LocationId, b.Lot })
            .IsUnique().HasDatabaseName("UX_stk_balances_key");
        builder.HasIndex(b => b.ItemId);
        builder.HasIndex(b => b.PartId);
        builder.HasIndex(b => b.BatchId);
        builder.HasIndex(b => b.LocationId);
    }
}
