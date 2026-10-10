namespace Barthazon.Shared.Kernel.Domain;

/// <summary>Entité métier : audit automatique, archivage et jeton de concurrence.</summary>
public abstract class AuditableEntity : Entity, IAuditable, IArchivable
{
    protected AuditableEntity()
    {
    }

    protected AuditableEntity(Guid id)
        : base(id)
    {
    }

    public DateTimeOffset CreatedAt { get; private set; }

    public string? CreatedBy { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public string? UpdatedBy { get; private set; }

    public bool IsArchived { get; private set; }

    public long Version { get; private set; }

    public void StampCreated(DateTimeOffset at, string? by)
    {
        CreatedAt = at;
        CreatedBy = by;
        UpdatedAt = at;
        UpdatedBy = by;
        Version = 1;
    }

    public void StampUpdated(DateTimeOffset at, string? by)
    {
        UpdatedAt = at;
        UpdatedBy = by;
        Version++;
    }

    public virtual void Archive() => IsArchived = true;

    public virtual void Restore() => IsArchived = false;
}
