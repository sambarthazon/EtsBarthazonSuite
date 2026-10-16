using Barthazon.Catalog.Contracts;
using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Stock.Core;
using Barthazon.Stock.Core.Persistence;
using Barthazon.Stock.Core.Services;
using Barthazon.Stock.Infrastructure;
using Barthazon.Stock.Tests;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Testing;

/// <summary>Horloge réglable par les tests (délais d'annulation, antidatage, lancements à l'arrêt).</summary>
public sealed class ManualTimeProvider : TimeProvider
{
    private static readonly TimeZoneInfo Paris = TimeZoneInfo.FindSystemTimeZoneById("Europe/Paris");

    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;

    public override DateTimeOffset GetUtcNow() => Now;

    public override TimeZoneInfo LocalTimeZone => Paris;

    public void Advance(TimeSpan delay) => Now += delay;
}

/// <summary>Hôte de test du stock : socle + module Stock + doublure de l'annuaire (<see cref="FakeCatalog"/>).</summary>
public sealed class StockTestApp : IAsyncDisposable
{
    private StockTestApp(TestApplication app, FakeCatalog catalog, ManualTimeProvider clock)
    {
        App = app;
        Catalog = catalog;
        Clock = clock;
    }

    public TestApplication App { get; }

    public FakeCatalog Catalog { get; }

    public ManualTimeProvider Clock { get; }

    public TestCurrentUser User => App.User;

    public IServiceProvider Services => App.Services;

    public T Get<T>()
        where T : notnull => App.Get<T>();

    public static async Task<StockTestApp> CreateAsync(Action<IServiceCollection>? configure = null, Dictionary<string, string?>? settings = null)
    {
        var catalog = new FakeCatalog();
        var clock = new ManualTimeProvider();
        var app = await TestApplication.CreateAsync(
            services =>
            {
                services.AddSingleton<TimeProvider>(clock);
                services.AddSingleton(catalog);
                services.AddSingleton<IPartLookup>(catalog);
                services.AddSingleton<IPartRoutingLookup>(catalog);
                configure?.Invoke(services);
            },
            settings,
            AddStock);
        return new StockTestApp(app, catalog, clock);
    }

    public static void AddStock(TestApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.AddModule(new StockPersistence(), s => s.AddStockCore())
            .WithSetting("Features:Stock", "true");
    }

    public void As(UserRole role, string userName = "testeur") => User.As(role, userName);

    /// <summary>Après chaque scénario : les soldes égalent la somme des lignes de mouvements (section 7.8).</summary>
    public async Task AssertBalancesMatchMovementsAsync()
    {
        await using var uow = Get<IUnitOfWorkFactory<IStockUnitOfWork>>().Create();
        (await BalanceVerificationService.FindDiscrepanciesAsync(uow, CancellationToken.None)).Should().BeEmpty("les soldes doivent égaler la somme des mouvements");
    }

    public ValueTask DisposeAsync() => App.DisposeAsync();
}
