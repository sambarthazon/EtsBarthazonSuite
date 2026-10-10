using Barthazon.Shared.Kernel.Domain;

namespace Barthazon.Referential.Core.Domain;

/// <summary>Client : donneur d'ordre, propriétaire des plans.</summary>
public sealed class Customer : AuditableEntity
{
    private readonly List<CustomerContact> _contacts = [];

    private Customer()
    {
    }

    public Customer(string code, string name)
    {
        Code = NormalizeCode(code);
        Name = name.Trim();
        IsActive = true;
    }

    /// <summary>Code court unique (ex. DUPONT), en majuscules.</summary>
    public string Code { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Address { get; set; }

    public string? PostalCode { get; set; }

    public string? City { get; set; }

    public string? Notes { get; set; }

    public bool IsActive { get; set; }

    public IReadOnlyList<CustomerContact> Contacts => _contacts;

    public void Rename(string code, string name)
    {
        Code = NormalizeCode(code);
        Name = name.Trim();
    }

    public CustomerContact AddContact(string name)
    {
        var contact = new CustomerContact(Id, name);
        _contacts.Add(contact);
        return contact;
    }

    public void RemoveContact(CustomerContact contact) => _contacts.Remove(contact);

    public static string NormalizeCode(string code) => (code ?? string.Empty).Trim().ToUpperInvariant();
}

/// <summary>Contact chez un client.</summary>
public sealed class CustomerContact : Entity, IAuditChild
{
    private CustomerContact()
    {
    }

    internal CustomerContact(Guid customerId, string name)
    {
        CustomerId = customerId;
        Name = name.Trim();
    }

    public Guid CustomerId { get; private set; }

    public string Name { get; set; } = string.Empty;

    public string? Function { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Notes { get; set; }

    public int Order { get; set; }

    public string AuditParentType => nameof(Customer);

    public Guid AuditParentId => CustomerId;
}
