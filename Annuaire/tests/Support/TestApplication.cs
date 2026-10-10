using Barthazon.Catalog.Core;
using Barthazon.Catalog.Infrastructure;
using Barthazon.Referential.Core;
using Barthazon.Referential.Infrastructure;
using Barthazon.Shared.Infrastructure;
using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Kernel.Security;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Barthazon.Testing;

/// <summary>Utilisateur courant modifiable par les tests.</summary>
public sealed class TestCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => Role is not null;

    public Guid? UserId { get; set; } = Guid.NewGuid();

    public string? UserName { get; set; } = "testeur";

    public string? DisplayName => UserName;

    public UserRole? Role { get; set; } = UserRole.Editor;

    public void SignOut() => Role = null;

    public void As(UserRole role, string userName = "testeur")
    {
        Role = role;
        UserName = userName;
    }
}

/// <summary>
/// Application complète (sans interface) sur une base SQLite temporaire, migrations réelles comprises.
/// </summary>
public sealed class TestApplication : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly IServiceScope _scope;

    private TestApplication(string root, ServiceProvider provider, TestCurrentUser user)
    {
        Root = root;
        _provider = provider;
        _scope = provider.CreateScope();
        User = user;
    }

    public string Root { get; }

    public TestCurrentUser User { get; }

    public IServiceProvider Services => _scope.ServiceProvider;

    public T Get<T>()
        where T : notnull => _scope.ServiceProvider.GetRequiredService<T>();

    public static async Task<TestApplication> CreateAsync(
        Action<IServiceCollection>? configure = null,
        Dictionary<string, string?>? settings = null)
    {
        var root = Path.Combine(Path.GetTempPath(), "bz-app-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var values = new Dictionary<string, string?>
        {
            ["App:PublicBaseUrl"] = "http://atelier:5080",
            ["Database:Provider"] = "Sqlite",
            ["Database:ConnectionString"] = $"Data Source={Path.Combine(root, "test.db")};Pooling=False",
            ["FileStorage:RootPath"] = Path.Combine(root, "files"),
            ["FileStorage:MaxFileSizeMb"] = "5",
            ["Backup:Directory"] = Path.Combine(root, "backups"),
            ["Catalog:ReferencePattern"] = "P-{0:0000}",
            ["Security:AllowAnonymousRead"] = "true",
        };
        foreach (var (key, value) in settings ?? [])
        {
            values[key] = value;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        var user = new TestCurrentUser();
        var services = new ServiceCollection();
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        services.AddSingleton<IConfiguration>(configuration);
        services.AddSingleton<ICurrentUser>(user);
        services.AddSharedInfrastructure(configuration, root, [new ReferentialPersistence(), new CatalogPersistence()]);
        services.AddReferentialCore();
        services.AddCatalogCore();
        services.AddSingleton<Barthazon.Shared.Infrastructure.Hosting.DatabaseHealthCheck>();
        configure?.Invoke(services);

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await provider.GetRequiredService<DatabaseInitializer>().InitializeAsync();
        return new TestApplication(root, provider, user);
    }

    public async ValueTask DisposeAsync()
    {
        _scope.Dispose();
        await _provider.DisposeAsync();
        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
