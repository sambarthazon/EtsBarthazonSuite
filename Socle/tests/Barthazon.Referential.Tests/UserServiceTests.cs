using Barthazon.Referential.Core.Services;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Testing;

namespace Barthazon.Referential.Tests;

public sealed class UserServiceTests : IAsyncLifetime
{
    private TestApplication _app = default!;

    private IUserService Users => _app.Get<IUserService>();

    public async Task InitializeAsync() => _app = await TestApplication.CreateAsync();

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task First_administrator_only_once()
    {
        (await Users.AnyUserAsync()).Should().BeFalse();

        var first = await Users.CreateFirstAdministratorAsync(new UserInput { UserName = "Admin", DisplayName = "Admin", Password = "motdepasse1" });
        var second = await Users.CreateFirstAdministratorAsync(new UserInput { UserName = "pirate", DisplayName = "Pirate", Password = "motdepasse1" });

        first.Value.Role.Should().Be(UserRole.Administrator);
        first.Value.UserName.Should().Be("admin");
        second.HasError(ErrorKind.Forbidden).Should().BeTrue();
    }

    [Fact]
    public async Task Authentication_checks_password_and_active_state()
    {
        await Users.CreateFirstAdministratorAsync(new UserInput { UserName = "admin", DisplayName = "Admin", Password = "motdepasse1" });
        _app.User.As(UserRole.Administrator, "admin");
        var op = (await Users.CreateAsync(new UserInput { UserName = "operateur", DisplayName = "Opérateur", Password = "atelier123", Role = UserRole.Reader })).Value;

        (await Users.AuthenticateAsync("OPERATEUR", "atelier123")).IsSuccess.Should().BeTrue();
        (await Users.AuthenticateAsync("operateur", "mauvais")).HasError(ErrorKind.Unauthenticated).Should().BeTrue();

        var input = new UserInput { UserName = op.UserName, DisplayName = op.DisplayName, Role = op.Role, IsActive = false, Version = op.Version };
        (await Users.UpdateAsync(op.Id, input)).IsSuccess.Should().BeTrue();
        (await Users.AuthenticateAsync("operateur", "atelier123")).IsFailure.Should().BeTrue();
    }

    [Fact]
    public async Task Last_administrator_cannot_be_demoted()
    {
        var admin = (await Users.CreateFirstAdministratorAsync(new UserInput { UserName = "admin", DisplayName = "Admin", Password = "motdepasse1" })).Value;
        _app.User.As(UserRole.Administrator, "admin");
        var item = (await Users.ListAsync()).Value.Single();

        var result = await Users.UpdateAsync(admin.Id, new UserInput { DisplayName = "Admin", Role = UserRole.Editor, IsActive = true, Version = item.Version });

        result.Errors.Should().ContainSingle(e => e.Field == nameof(UserInput.Role));
    }

    [Fact]
    public async Task Only_administrators_manage_users()
    {
        _app.User.As(UserRole.Editor);

        (await Users.ListAsync()).HasError(ErrorKind.Forbidden).Should().BeTrue();
        (await Users.CreateAsync(new UserInput { UserName = "x1", DisplayName = "X", Password = "12345678" })).HasError(ErrorKind.Forbidden).Should().BeTrue();
    }

    [Fact]
    public async Task Password_must_be_long_enough()
    {
        var result = await Users.CreateFirstAdministratorAsync(new UserInput { UserName = "admin", DisplayName = "Admin", Password = "court" });

        result.Errors.Should().ContainSingle(e => e.Field == nameof(UserInput.Password));
    }
}
