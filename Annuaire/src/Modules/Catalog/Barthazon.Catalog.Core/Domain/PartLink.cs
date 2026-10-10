using Barthazon.Catalog.Contracts;
using Barthazon.Shared.Kernel.Domain;

namespace Barthazon.Catalog.Core.Domain;

/// <summary>Lien entre deux pièces ; le lien inverse est affiché automatiquement sur l'autre fiche.</summary>
public sealed class PartLink : AuditableEntity, IAuditChild
{
    private PartLink()
    {
    }

    public PartLink(Guid sourcePartId, Guid targetPartId, PartLinkType type)
    {
        SourcePartId = sourcePartId;
        TargetPartId = targetPartId;
        Type = type;
    }

    public Guid SourcePartId { get; private set; }

    public Guid TargetPartId { get; private set; }

    public PartLinkType Type { get; set; }

    public decimal? Quantity { get; set; }

    public string? Note { get; set; }

    public string AuditParentType => nameof(Part);

    public Guid AuditParentId => SourcePartId;
}

/// <summary>Pièce favorite d'un utilisateur connecté (les visiteurs non connectés utilisent le stockage du navigateur).</summary>
public sealed class Favorite
{
    private Favorite()
    {
    }

    public Favorite(Guid userId, Guid partId, DateTimeOffset createdAt)
    {
        UserId = userId;
        PartId = partId;
        CreatedAt = createdAt;
    }

    public Guid UserId { get; private set; }

    public Guid PartId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
