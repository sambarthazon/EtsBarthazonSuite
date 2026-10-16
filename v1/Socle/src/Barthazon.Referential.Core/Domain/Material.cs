using Barthazon.Referential.Contracts;
using Barthazon.Shared.Kernel.Domain;

namespace Barthazon.Referential.Core.Domain;

/// <summary>Matière et nuance.</summary>
public sealed class Material : AuditableEntity
{
    private Material()
    {
    }

    public Material(string designation, string? grade, MaterialFamily family)
    {
        Designation = designation.Trim();
        Grade = grade?.Trim();
        Family = family;
    }

    /// <summary>Désignation (ex. « Acier C45 »).</summary>
    public string Designation { get; set; } = string.Empty;

    /// <summary>Nuance / norme (ex. 1.0503, 42CrMo4, 2017A).</summary>
    public string? Grade { get; set; }

    public MaterialFamily Family { get; set; }

    public decimal? DensityKgDm3 { get; set; }

    public string? Notes { get; set; }
}

/// <summary>Machine ou poste de travail.</summary>
public sealed class Machine : AuditableEntity
{
    private Machine()
    {
    }

    public Machine(string name, MachineType type)
    {
        Name = name.Trim();
        Type = type;
        IsActive = true;
    }

    public string Name { get; set; } = string.Empty;

    public MachineType Type { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; }
}
