using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Resources;
using Barthazon.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Shared.Infrastructure.Persistence;

/// <summary>Unité de travail EF Core : possède un contexte de courte durée.</summary>
public abstract class EfUnitOfWork : IUnitOfWork
{
    protected EfUnitOfWork(AppDbContext context, string? userName)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        Context.AuditUserName = userName;
    }

    protected AppDbContext Context { get; }

    public async Task<Result> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        // Filet de sécurité : un texte plus long que la colonne donne un message clair, quel que soit le fournisseur
        // (SQLite ignore les longueurs, PostgreSQL refuserait l'enregistrement avec une exception).
        var tooLong = TooLongValues();
        if (tooLong.Count > 0)
        {
            return Result.Failure(tooLong);
        }

        try
        {
            await Context.SaveChangesAsync(cancellationToken);
            return Result.Success();
        }
        catch (DbUpdateConcurrencyException)
        {
            return Result.Failure(Error.Conflict(KernelTexts.ConcurrencyConflict));
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            return Result.Failure(Error.Conflict(KernelTexts.UniqueConflict));
        }
    }

    public void SetExpectedVersion(Kernel.Domain.IAuditable entity, long expectedVersion)
    {
        ArgumentNullException.ThrowIfNull(entity);
        Context.Entry(entity).Property(nameof(Kernel.Domain.IAuditable.Version)).OriginalValue = expectedVersion;
    }

    public void AddAuditNote(string entityType, Guid entityId, string action, string? detail = null, string? parentType = null, Guid? parentId = null) =>
        Context.AddAuditNote(entityType, entityId, action, detail, parentType, parentId);

    public ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return Context.DisposeAsync();
    }

    private List<Error> TooLongValues()
    {
        var errors = new List<Error>();
        foreach (var entry in Context.ChangeTracker.Entries().Where(e => e.State is EntityState.Added or EntityState.Modified))
        {
            foreach (var property in entry.Properties)
            {
                if (property.CurrentValue is string value
                    && property.Metadata.GetMaxLength() is { } max
                    && value.Length > max
                    && (entry.State == EntityState.Added || property.IsModified))
                {
                    errors.Add(Error.Validation(
                        string.Format(System.Globalization.CultureInfo.CurrentCulture, KernelTexts.TextTooLongMax, max),
                        property.Metadata.Name));
                }
            }
        }

        return errors;
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
    {
        var message = ex.InnerException?.Message ?? ex.Message;
        return message.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
            || message.Contains("duplicate key value", StringComparison.OrdinalIgnoreCase)
            || message.Contains("23505", StringComparison.Ordinal);
    }
}
