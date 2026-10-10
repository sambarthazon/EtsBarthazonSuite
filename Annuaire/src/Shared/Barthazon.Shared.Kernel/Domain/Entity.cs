namespace Barthazon.Shared.Kernel.Domain;

/// <summary>Base de toutes les entités : identifiant <see cref="Guid"/>.</summary>
public abstract class Entity
{
    protected Entity()
    {
        Id = Guid.NewGuid();
    }

    protected Entity(Guid id)
    {
        Id = id;
    }

    public Guid Id { get; protected set; }
}
