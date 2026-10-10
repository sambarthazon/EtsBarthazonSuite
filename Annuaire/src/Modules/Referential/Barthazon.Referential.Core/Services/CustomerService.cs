using System.Text.RegularExpressions;
using Barthazon.Referential.Contracts;
using Barthazon.Referential.Core.Domain;
using Barthazon.Referential.Core.Persistence;
using Barthazon.Referential.Core.Resources;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Shared.Kernel.Text;

namespace Barthazon.Referential.Core.Services;

/// <summary>Gestion des clients et de leurs contacts.</summary>
public interface ICustomerService
{
    Task<Result<IReadOnlyList<CustomerListItem>>> ListAsync(string? query, bool includeArchived, CancellationToken cancellationToken = default);

    Task<Result<CustomerDetails>> GetByCodeAsync(string code, CancellationToken cancellationToken = default);

    Task<Result<CustomerDetails>> CreateAsync(CustomerInput input, CancellationToken cancellationToken = default);

    Task<Result<CustomerDetails>> UpdateAsync(Guid id, CustomerInput input, CancellationToken cancellationToken = default);

    Task<Result> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default);
}

internal sealed partial class CustomerService(
    IUnitOfWorkFactory<IReferentialUnitOfWork> unitOfWorkFactory,
    IAccessGuard accessGuard,
    UsageCounter usageCounter,
    IEnumerable<IReferentialChangeObserver> observers) : ICustomerService, ICustomerLookup
{
    public async Task<Result<IReadOnlyList<CustomerListItem>>> ListAsync(string? query, bool includeArchived, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanRead();
        if (allowed.IsFailure)
        {
            return Result<IReadOnlyList<CustomerListItem>>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var customers = await uow.Customers.ListAsync(includeArchived, cancellationToken);
        var usages = await usageCounter.CountAsync(ReferentialItemKind.Customer, cancellationToken);
        var items = customers
            .Where(c => SearchNormalizer.Matches(SearchText(c), query))
            .OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .Select(c => new CustomerListItem(c.Id, c.Code, c.Name, c.City, c.IsActive, c.IsArchived, usages.GetValueOrDefault(c.Id)))
            .ToList();
        return Result.Success<IReadOnlyList<CustomerListItem>>(items);
    }

    public async Task<Result<CustomerDetails>> GetByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanRead();
        if (allowed.IsFailure)
        {
            return Result<CustomerDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var customer = await uow.Customers.GetByCodeAsync(Customer.NormalizeCode(code), cancellationToken);
        if (customer is null)
        {
            return Error.NotFound(ReferentialMessages.CustomerNotFound);
        }

        return await ToDetailsAsync(customer, cancellationToken);
    }

    public async Task<Result<CustomerDetails>> CreateAsync(CustomerInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<CustomerDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var validation = await ValidateAsync(uow, input, null, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<CustomerDetails>.Failure(validation.Errors);
        }

        var customer = new Customer(input.Code, input.Name);
        Apply(customer, input);
        uow.Customers.Add(customer);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? await ToDetailsAsync(customer, cancellationToken) : Result<CustomerDetails>.Failure(saved.Errors);
    }

    public async Task<Result<CustomerDetails>> UpdateAsync(Guid id, CustomerInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<CustomerDetails>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var customer = await uow.Customers.GetWithContactsAsync(id, cancellationToken);
        if (customer is null)
        {
            return Error.NotFound(ReferentialMessages.CustomerNotFound);
        }

        var validation = await ValidateAsync(uow, input, id, cancellationToken);
        if (!validation.IsValid)
        {
            return Result<CustomerDetails>.Failure(validation.Errors);
        }

        uow.Customers.SetExpectedVersion(customer, input.Version);
        customer.Rename(input.Code, input.Name);
        Apply(customer, input);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result<CustomerDetails>.Failure(saved.Errors);
        }

        foreach (var observer in observers)
        {
            await observer.OnCustomerChangedAsync(customer.Id, cancellationToken);
        }

        return await ToDetailsAsync(customer, cancellationToken);
    }

    public async Task<Result> SetArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        await using var uow = unitOfWorkFactory.Create();
        var customer = await uow.Customers.GetByIdAsync(id, cancellationToken);
        if (customer is null)
        {
            return Result.Failure(Error.NotFound(ReferentialMessages.CustomerNotFound));
        }

        if (archived)
        {
            customer.Archive();
        }
        else
        {
            customer.Restore();
        }

        return await uow.SaveChangesAsync(cancellationToken);
    }

    // ----- ICustomerLookup (contrat pour les autres modules) -----
    public async Task<CustomerSummary?> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await using var uow = unitOfWorkFactory.Create();
        var customer = await uow.Customers.GetByIdAsync(id, cancellationToken);
        return customer is null ? null : ToSummary(customer);
    }

    async Task<CustomerSummary?> ICustomerLookup.GetByCodeAsync(string code, CancellationToken cancellationToken)
    {
        await using var uow = unitOfWorkFactory.Create();
        var customer = await uow.Customers.GetByCodeAsync(Customer.NormalizeCode(code), cancellationToken);
        return customer is null ? null : ToSummary(customer);
    }

    public async Task<IReadOnlyDictionary<Guid, CustomerSummary>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
    {
        var distinct = ids.Distinct().ToList();
        if (distinct.Count == 0)
        {
            return new Dictionary<Guid, CustomerSummary>();
        }

        await using var uow = unitOfWorkFactory.Create();
        var customers = await uow.Customers.GetManyAsync(distinct, cancellationToken);
        return customers.ToDictionary(c => c.Id, ToSummary);
    }

    public async Task<IReadOnlyList<CustomerSummary>> SearchAsync(string? query, int maxResults = 20, CancellationToken cancellationToken = default)
    {
        await using var uow = unitOfWorkFactory.Create();
        var customers = await uow.Customers.ListAsync(includeArchived: false, cancellationToken);
        return customers
            .Where(c => c.IsActive && SearchNormalizer.Matches(SearchText(c), query))
            .OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .Select(ToSummary)
            .ToList();
    }

    async Task<IReadOnlyList<CustomerSummary>> ICustomerLookup.ListAsync(bool includeArchived, CancellationToken cancellationToken)
    {
        await using var uow = unitOfWorkFactory.Create();
        var customers = await uow.Customers.ListAsync(includeArchived, cancellationToken);
        return customers.OrderBy(c => c.Code, StringComparer.OrdinalIgnoreCase).Select(ToSummary).ToList();
    }

    public async Task<Result<CustomerSummary>> QuickCreateAsync(string code, string name, CancellationToken cancellationToken = default)
    {
        var created = await CreateAsync(new CustomerInput { Code = code, Name = name }, cancellationToken);
        return created.IsSuccess
            ? Result.Success(new CustomerSummary(created.Value.Id, created.Value.Code, created.Value.Name, created.Value.City, true, false))
            : Result<CustomerSummary>.Failure(created.Errors);
    }

    private static CustomerSummary ToSummary(Customer c) => new(c.Id, c.Code, c.Name, c.City, c.IsActive, c.IsArchived);

    private static string SearchText(Customer c) => SearchNormalizer.BuildSearchText([c.Code, c.Name, c.City]);

    private async Task<Result<CustomerDetails>> ToDetailsAsync(Customer c, CancellationToken cancellationToken)
    {
        var usages = await usageCounter.CountAsync(ReferentialItemKind.Customer, cancellationToken);
        return new CustomerDetails(
            c.Id, c.Code, c.Name, c.Address, c.PostalCode, c.City, c.Notes, c.IsActive, c.IsArchived, c.Version,
            c.Contacts.OrderBy(x => x.Order).Select(x => new ContactDto(x.Id, x.Name, x.Function, x.Phone, x.Email, x.Notes)).ToList(),
            c.UpdatedAt, c.UpdatedBy, usages.GetValueOrDefault(c.Id));
    }

    private static void Apply(Customer customer, CustomerInput input)
    {
        customer.Address = Inputs.Clean(input.Address);
        customer.PostalCode = Inputs.Clean(input.PostalCode);
        customer.City = Inputs.Clean(input.City);
        customer.Notes = Inputs.Clean(input.Notes);
        customer.IsActive = input.IsActive;

        var keep = input.Contacts.Where(c => c.Id is not null).Select(c => c.Id!.Value).ToHashSet();
        foreach (var removed in customer.Contacts.Where(c => !keep.Contains(c.Id)).ToList())
        {
            customer.RemoveContact(removed);
        }

        var order = 0;
        foreach (var contactInput in input.Contacts)
        {
            var contact = contactInput.Id is { } id ? customer.Contacts.FirstOrDefault(c => c.Id == id) : null;
            contact ??= customer.AddContact(contactInput.Name);
            contact.Name = Inputs.CleanRequired(contactInput.Name);
            contact.Function = Inputs.Clean(contactInput.Function);
            contact.Phone = Inputs.Clean(contactInput.Phone);
            contact.Email = Inputs.Clean(contactInput.Email);
            contact.Notes = Inputs.Clean(contactInput.Notes);
            contact.Order = order++;
        }
    }

    private static async Task<Validation> ValidateAsync(IReferentialUnitOfWork uow, CustomerInput input, Guid? id, CancellationToken cancellationToken)
    {
        var code = Customer.NormalizeCode(input.Code);
        var validation = new Validation()
            .Required(code, nameof(CustomerInput.Code), ReferentialMessages.CustomerCodeRequired)
            .MaxLength(code, 20, nameof(CustomerInput.Code), ReferentialMessages.CustomerCodeTooLong)
            .When(code.Length > 0 && !CodePattern().IsMatch(code), nameof(CustomerInput.Code), ReferentialMessages.CustomerCodeInvalid)
            .Required(input.Name, nameof(CustomerInput.Name), ReferentialMessages.CustomerNameRequired)
            .MaxLength(input.Name, 200, nameof(CustomerInput.Name), ReferentialMessages.TextTooLong)
            .MaxLength(input.Address, 300, nameof(CustomerInput.Address), ReferentialMessages.TextTooLong)
            .MaxLength(input.City, 100, nameof(CustomerInput.City), ReferentialMessages.TextTooLong)
            .MaxLength(input.PostalCode, 20, nameof(CustomerInput.PostalCode), ReferentialMessages.TextTooLong)
            .MaxLength(input.Notes, 4000, nameof(CustomerInput.Notes), ReferentialMessages.TextTooLong);
        for (var i = 0; i < input.Contacts.Count; i++)
        {
            var contact = input.Contacts[i];
            validation
                .Required(contact.Name, $"Contacts[{i}].Name", ReferentialMessages.ContactNameRequired)
                .Email(contact.Email, $"Contacts[{i}].Email", ReferentialMessages.ContactEmailInvalid)
                .MaxLength(contact.Name, 150, $"Contacts[{i}].Name", ReferentialMessages.TextTooLong);
        }

        if (validation.IsValid && await uow.Customers.CodeExistsAsync(code, id, cancellationToken))
        {
            validation.Add(nameof(CustomerInput.Code), ReferentialMessages.CustomerCodeExists);
        }

        return validation;
    }

    [GeneratedRegex("^[A-Z0-9_-]+$")]
    private static partial Regex CodePattern();
}
