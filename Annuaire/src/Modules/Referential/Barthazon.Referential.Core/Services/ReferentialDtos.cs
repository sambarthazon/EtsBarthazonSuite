using Barthazon.Referential.Contracts;

namespace Barthazon.Referential.Core.Services;

/// <summary>Ligne de la liste des clients.</summary>
public sealed record CustomerListItem(Guid Id, string Code, string Name, string? City, bool IsActive, bool IsArchived, int UsageCount);

/// <summary>Contact d'un client.</summary>
public sealed record ContactDto(Guid Id, string Name, string? Function, string? Phone, string? Email, string? Notes);

/// <summary>Fiche client.</summary>
public sealed record CustomerDetails(
    Guid Id,
    string Code,
    string Name,
    string? Address,
    string? PostalCode,
    string? City,
    string? Notes,
    bool IsActive,
    bool IsArchived,
    long Version,
    IReadOnlyList<ContactDto> Contacts,
    DateTimeOffset UpdatedAt,
    string? UpdatedBy,
    int UsageCount);

/// <summary>Saisie d'un client (formulaire).</summary>
public sealed class CustomerInput
{
    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Address { get; set; }

    public string? PostalCode { get; set; }

    public string? City { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public List<ContactInput> Contacts { get; set; } = [];

    /// <summary>Version connue (concurrence optimiste) ; ignorée à la création.</summary>
    public long Version { get; set; }

    public static CustomerInput From(CustomerDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);
        return new CustomerInput
        {
            Code = details.Code,
            Name = details.Name,
            Address = details.Address,
            PostalCode = details.PostalCode,
            City = details.City,
            Notes = details.Notes,
            IsActive = details.IsActive,
            Version = details.Version,
            Contacts = details.Contacts.Select(c => new ContactInput
            {
                Id = c.Id,
                Name = c.Name,
                Function = c.Function,
                Phone = c.Phone,
                Email = c.Email,
                Notes = c.Notes,
            }).ToList(),
        };
    }
}

/// <summary>Saisie d'un contact.</summary>
public sealed class ContactInput
{
    public Guid? Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? Function { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Notes { get; set; }
}

/// <summary>Matière (liste et édition).</summary>
public sealed record MaterialItem(Guid Id, string Designation, string? Grade, MaterialFamily Family, decimal? DensityKgDm3, string? Notes, bool IsArchived, long Version, int UsageCount);

/// <summary>Saisie d'une matière.</summary>
public sealed class MaterialInput
{
    public string Designation { get; set; } = string.Empty;

    public string? Grade { get; set; }

    public MaterialFamily Family { get; set; } = MaterialFamily.Steel;

    public decimal? DensityKgDm3 { get; set; }

    public string? Notes { get; set; }

    public long Version { get; set; }
}

/// <summary>Machine (liste et édition).</summary>
public sealed record MachineItem(Guid Id, string Name, MachineType Type, string? Notes, bool IsActive, bool IsArchived, long Version, int UsageCount);

/// <summary>Saisie d'une machine.</summary>
public sealed class MachineInput
{
    public string Name { get; set; } = string.Empty;

    public MachineType Type { get; set; } = MachineType.CncLathe;

    public string? Notes { get; set; }

    public bool IsActive { get; set; } = true;

    public long Version { get; set; }
}
