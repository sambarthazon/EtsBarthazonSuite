using System.Reflection;
using System.Text.Json;
using Barthazon.Shared.Kernel.Auditing;
using Barthazon.Shared.Kernel.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Barthazon.Shared.Infrastructure.Persistence;

/// <summary>
/// Contexte EF Core unique, qui charge les mappings de chaque module déclaré.
/// À utiliser uniquement via <see cref="AppDbContextFactory"/> : un contexte de courte durée par opération applicative.
/// L'enregistrement remplit automatiquement les champs d'audit et alimente le journal.
/// </summary>
public sealed class AppDbContext : DbContext
{
    /// <summary>Propriétés techniques jamais tracées dans le détail du journal.</summary>
    private static readonly HashSet<string> IgnoredAuditProperties =
    [
        nameof(IAuditable.Version), nameof(IAuditable.CreatedAt), nameof(IAuditable.CreatedBy),
        nameof(IAuditable.UpdatedAt), nameof(IAuditable.UpdatedBy), "SearchText", "Id", "LastLoginAt",
    ];

    /// <summary>Propriétés dont la valeur est masquée dans le journal.</summary>
    private static readonly HashSet<string> MaskedAuditProperties = ["PasswordHash", "SecurityStamp"];

    private readonly IReadOnlyList<Assembly> _mappingAssemblies;
    private readonly TimeProvider _timeProvider;

    public AppDbContext(DbContextOptions<AppDbContext> options, IReadOnlyList<Assembly> mappingAssemblies, TimeProvider? timeProvider = null)
        : base(options)
    {
        _mappingAssemblies = mappingAssemblies;
        _timeProvider = timeProvider ?? TimeProvider.System;
        ModelKey = string.Join('|', mappingAssemblies.Select(a => a.GetName().Name).Order(StringComparer.Ordinal));
    }

    /// <summary>Clé du modèle (assemblages de mappings), utilisée par le cache de modèles d'EF Core.</summary>
    internal string ModelKey { get; }

    /// <summary>Identifiant de connexion de l'auteur des modifications (rempli par l'unité de travail).</summary>
    public string? AuditUserName { get; set; }

    /// <summary>Désactive l'audit et l'horodatage (import complet de données).</summary>
    public bool SuppressAudit { get; set; }

    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        PrepareAudit();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        PrepareAudit();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>Ajoute une entrée explicite au journal (ex. « mise en vigueur »).</summary>
    public void AddAuditNote(string entityType, Guid entityId, string action, string? detail, string? parentType, Guid? parentId)
    {
        AuditEntries.Add(new AuditEntry
        {
            At = _timeProvider.GetUtcNow(),
            UserName = AuditUserName,
            EntityType = entityType,
            EntityId = entityId,
            ParentType = parentType,
            ParentId = parentId,
            Action = action,
            Details = detail,
        });
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        ArgumentNullException.ThrowIfNull(configurationBuilder);
        if (Database.IsSqlite())
        {
            // Limites de SQLite : tri / comparaison impossibles sur decimal et DateTimeOffset.
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToUtcTicksConverter>();
            configurationBuilder.Properties<decimal>().HaveConversion<double>();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        foreach (var assembly in _mappingAssemblies.Where(a => a != typeof(AppDbContext).Assembly))
        {
            modelBuilder.ApplyConfigurationsFromAssembly(assembly);
        }

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(Entity).IsAssignableFrom(entityType.ClrType))
            {
                // Identifiants générés par le code : un enfant ajouté à une collection doit être inséré,
                // pas pris pour une ligne existante.
                modelBuilder.Entity(entityType.ClrType).Property(nameof(Entity.Id)).ValueGeneratedNever();
            }

            if (typeof(IAuditable).IsAssignableFrom(entityType.ClrType))
            {
                var builder = modelBuilder.Entity(entityType.ClrType);
                builder.Property(nameof(IAuditable.Version)).IsConcurrencyToken();
                builder.Property(nameof(IAuditable.CreatedBy)).HasMaxLength(100);
                builder.Property(nameof(IAuditable.UpdatedBy)).HasMaxLength(100);
            }
        }
    }

    private void PrepareAudit()
    {
        if (SuppressAudit)
        {
            return;
        }

        ChangeTracker.DetectChanges();
        var now = _timeProvider.GetUtcNow();
        var entries = ChangeTracker.Entries()
            .Where(e => e.Entity is not AuditEntry && e.State is EntityState.Added or EntityState.Modified or EntityState.Deleted)
            .ToList();

        foreach (var entry in entries)
        {
            var changes = DescribeChanges(entry);
            if (entry.State == EntityState.Modified && changes.Count == 0)
            {
                continue;
            }

            if (entry.Entity is IAuditable auditable)
            {
                if (entry.State == EntityState.Added)
                {
                    auditable.StampCreated(now, AuditUserName);
                }
                else if (entry.State == EntityState.Modified)
                {
                    auditable.StampUpdated(now, AuditUserName);
                }
            }

            if (entry.Entity is not Entity entity)
            {
                continue;
            }

            var child = entry.Entity as IAuditChild;
            AuditEntries.Add(new AuditEntry
            {
                At = now,
                UserName = AuditUserName,
                EntityType = entry.Metadata.ClrType.Name,
                EntityId = entity.Id,
                ParentType = child?.AuditParentType,
                ParentId = child?.AuditParentId,
                Action = ActionFor(entry, changes),
                Details = changes.Count == 0 ? null : JsonSerializer.Serialize(changes),
            });
        }
    }

    private static string ActionFor(EntityEntry entry, Dictionary<string, AuditChange> changes)
    {
        return entry.State switch
        {
            EntityState.Added => AuditActions.Created,
            EntityState.Deleted => AuditActions.Deleted,
            _ when changes.TryGetValue(nameof(IArchivable.IsArchived), out var archived) && changes.Count == 1 =>
                archived.New is true ? AuditActions.Archived : AuditActions.Restored,
            _ => AuditActions.Updated,
        };
    }

    private static Dictionary<string, AuditChange> DescribeChanges(EntityEntry entry)
    {
        var changes = new Dictionary<string, AuditChange>(StringComparer.Ordinal);
        foreach (var property in entry.Properties)
        {
            var name = property.Metadata.Name;
            if (IgnoredAuditProperties.Contains(name) || property.Metadata.IsShadowProperty())
            {
                continue;
            }

            var masked = MaskedAuditProperties.Contains(name);
            switch (entry.State)
            {
                case EntityState.Added when property.CurrentValue is not null && !IsDefault(property.CurrentValue):
                    changes[name] = new AuditChange(null, masked ? "***" : Simplify(property.CurrentValue));
                    break;
                case EntityState.Deleted when property.OriginalValue is not null:
                    changes[name] = new AuditChange(masked ? "***" : Simplify(property.OriginalValue), null);
                    break;
                case EntityState.Modified when property.IsModified && !Equals(property.OriginalValue, property.CurrentValue):
                    changes[name] = masked
                        ? new AuditChange("***", "***")
                        : new AuditChange(Simplify(property.OriginalValue), Simplify(property.CurrentValue));
                    break;
            }
        }

        return changes;
    }

    private static bool IsDefault(object value) => value switch
    {
        bool b => !b,
        string s => s.Length == 0,
        Guid g => g == Guid.Empty,
        _ => false,
    };

    private static object? Simplify(object? value) => value switch
    {
        null => null,
        Enum e => e.ToString(),
        DateOnly d => d.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        DateTimeOffset d => d.ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        _ => value,
    };
}

/// <summary>Modification d'un champ (ancienne → nouvelle valeur), sérialisée dans le journal.</summary>
public sealed record AuditChange(object? Old, object? New);

/// <summary>Le cache de modèles d'EF Core doit distinguer les ensembles de modules (tests, outils).</summary>
internal sealed class AppModelCacheKeyFactory : IModelCacheKeyFactory
{
    public object Create(DbContext context, bool designTime) =>
        context is AppDbContext app
            ? (context.GetType(), app.ModelKey, context.Database.ProviderName, designTime)
            : (object)(context.GetType(), designTime);
}
