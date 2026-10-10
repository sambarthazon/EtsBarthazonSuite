using Barthazon.Referential.Contracts;
using Barthazon.Referential.Core.Services;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Testing;

namespace Barthazon.Referential.Tests;

public sealed class CustomerServiceTests : IAsyncLifetime
{
    private TestApplication _app = default!;

    private ICustomerService Customers => _app.Get<ICustomerService>();

    public async Task InitializeAsync() => _app = await TestApplication.CreateAsync();

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Create_normalizes_code_and_saves_contacts()
    {
        var result = await Customers.CreateAsync(new CustomerInput
        {
            Code = " dupont ",
            Name = "Dupont Industrie",
            Contacts = [new ContactInput { Name = "Jean", Email = "jean@dupont.fr" }],
        });

        result.IsSuccess.Should().BeTrue();
        result.Value.Code.Should().Be("DUPONT");
        result.Value.Contacts.Should().ContainSingle(c => c.Name == "Jean");
    }

    [Fact]
    public async Task Duplicate_code_is_refused_on_code_field()
    {
        await Customers.CreateAsync(new CustomerInput { Code = "ACME", Name = "Acme" });

        var result = await Customers.CreateAsync(new CustomerInput { Code = "acme", Name = "Autre" });

        result.IsFailure.Should().BeTrue();
        result.Errors.Should().ContainSingle(e => e.Field == nameof(CustomerInput.Code));
    }

    [Fact]
    public async Task Invalid_input_reports_each_field()
    {
        var result = await Customers.CreateAsync(new CustomerInput
        {
            Code = "AB CD",
            Name = " ",
            Contacts = [new ContactInput { Name = "", Email = "pas-un-mail" }],
        });

        result.Errors.Select(e => e.Field).Should().BeEquivalentTo(
            [nameof(CustomerInput.Code), nameof(CustomerInput.Name), "Contacts[0].Name", "Contacts[0].Email"]);
    }

    [Fact]
    public async Task Reader_cannot_create_but_can_list()
    {
        _app.User.As(UserRole.Reader);

        var created = await Customers.CreateAsync(new CustomerInput { Code = "X", Name = "X" });
        var listed = await Customers.ListAsync(null, includeArchived: false);

        created.HasError(ErrorKind.Forbidden).Should().BeTrue();
        listed.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Anonymous_read_can_be_disabled()
    {
        await using var app = await TestApplication.CreateAsync(settings: new() { ["Security:AllowAnonymousRead"] = "false" });
        app.User.SignOut();

        var listed = await app.Get<ICustomerService>().ListAsync(null, false);

        listed.HasError(ErrorKind.Unauthenticated).Should().BeTrue();
    }

    [Fact]
    public async Task Update_with_stale_version_is_a_conflict()
    {
        var created = (await Customers.CreateAsync(new CustomerInput { Code = "C1", Name = "Client 1" })).Value;
        var first = CustomerInput.From(created);
        var second = CustomerInput.From(created);
        first.Name = "Premier";
        second.Name = "Second";

        (await Customers.UpdateAsync(created.Id, first)).IsSuccess.Should().BeTrue();
        var conflict = await Customers.UpdateAsync(created.Id, second);

        conflict.HasError(ErrorKind.Conflict).Should().BeTrue();
    }

    [Fact]
    public async Task Update_adds_modifies_and_removes_contacts()
    {
        var created = (await Customers.CreateAsync(new CustomerInput
        {
            Code = "C2",
            Name = "Client 2",
            Contacts = [new ContactInput { Name = "A" }, new ContactInput { Name = "B" }],
        })).Value;
        var input = CustomerInput.From(created);
        input.Contacts.RemoveAt(0);
        input.Contacts[0].Phone = "0102030405";
        input.Contacts.Add(new ContactInput { Name = "C" });

        var updated = await Customers.UpdateAsync(created.Id, input);

        updated.Value.Contacts.Select(c => c.Name).Should().Equal("B", "C");
        updated.Value.Contacts[0].Phone.Should().Be("0102030405");
    }

    [Fact]
    public async Task Search_is_tolerant_and_archived_are_hidden_by_default()
    {
        await Customers.CreateAsync(new CustomerInput { Code = "STE", Name = "Société Générale de Mécanique", City = "Saint-Étienne" });
        var old = (await Customers.CreateAsync(new CustomerInput { Code = "OLD", Name = "Ancienne mécanique" })).Value;
        await Customers.SetArchivedAsync(old.Id, true);

        (await Customers.ListAsync("mecanique", false)).Value.Select(c => c.Code).Should().Equal("STE");
        (await Customers.ListAsync("saint etienne", false)).Value.Should().ContainSingle();
        (await Customers.ListAsync("mecanique", true)).Value.Should().HaveCount(2);
    }

    [Fact]
    public async Task Quick_create_through_contract()
    {
        var lookup = _app.Get<ICustomerLookup>();

        var created = await lookup.QuickCreateAsync("rapide", "Client rapide");

        created.Value.Code.Should().Be("RAPIDE");
        (await lookup.GetByCodeAsync("Rapide"))!.Name.Should().Be("Client rapide");
    }
}
