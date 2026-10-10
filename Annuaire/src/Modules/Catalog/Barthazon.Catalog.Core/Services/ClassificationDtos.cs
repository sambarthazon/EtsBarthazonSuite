using Barthazon.Catalog.Contracts;

namespace Barthazon.Catalog.Core.Services;

/// <summary>Famille (liste et édition).</summary>
public sealed record FamilyItem(Guid Id, string Name, string? Description, bool IsArchived, long Version, int UsageCount);

/// <summary>Saisie d'une famille.</summary>
public sealed class FamilyInput
{
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public long Version { get; set; }
}

/// <summary>Étiquette (liste et édition).</summary>
public sealed record TagItem(Guid Id, string Label, TagColor Color, bool IsArchived, long Version, int UsageCount);

/// <summary>Saisie d'une étiquette.</summary>
public sealed class TagInput
{
    public string Label { get; set; } = string.Empty;

    public TagColor Color { get; set; } = TagColor.Primary;

    public long Version { get; set; }
}
