using Barthazon.Catalog.Contracts;
using Barthazon.Shared.Kernel.Domain;

namespace Barthazon.Catalog.Core.Domain;

/// <summary>Famille de pièces (arbre, bague, bride, axe, support…).</summary>
public sealed class PartFamily : AuditableEntity
{
    private PartFamily()
    {
    }

    public PartFamily(string name)
    {
        Name = name.Trim();
    }

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }
}

/// <summary>Étiquette (mot-clé libre) avec une couleur de la charte.</summary>
public sealed class Tag : AuditableEntity
{
    private Tag()
    {
    }

    public Tag(string label, TagColor color)
    {
        Label = label.Trim();
        Color = color;
    }

    public string Label { get; set; } = string.Empty;

    public TagColor Color { get; set; }
}
