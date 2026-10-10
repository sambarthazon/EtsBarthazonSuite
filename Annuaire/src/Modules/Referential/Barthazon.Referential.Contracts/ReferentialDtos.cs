namespace Barthazon.Referential.Contracts;

/// <summary>Client, vu par les autres modules.</summary>
public sealed record CustomerSummary(Guid Id, string Code, string Name, string? City, bool IsActive, bool IsArchived)
{
    public string Display => $"{Code} — {Name}";
}

/// <summary>Matière, vue par les autres modules.</summary>
public sealed record MaterialSummary(Guid Id, string Designation, string? Grade, MaterialFamily Family, decimal? DensityKgDm3, bool IsArchived)
{
    public string Display => string.IsNullOrWhiteSpace(Grade) ? Designation : $"{Designation} ({Grade})";
}

/// <summary>Machine, vue par les autres modules.</summary>
public sealed record MachineSummary(Guid Id, string Name, MachineType Type, bool IsActive, bool IsArchived);
