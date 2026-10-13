using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Infrastructure.Persistence;
using Barthazon.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Stock.Tests;

/// <summary>Jalon S0 : base du stock (contexte, migrations, historique propres) et données de première installation.</summary>
public sealed class SkeletonTests : IAsyncLifetime
{
    private StockTestApp _app = default!;

    public async Task InitializeAsync() => _app = await StockTestApp.CreateAsync();

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Stock_database_has_its_own_migrations_and_history_table()
    {
        var database = _app.Services.GetServices<ModuleDatabase>().Single(d => d.Info.Key == "stock");
        database.Info.HistoryTable.Should().Be("__ef_migrations_stock");
        database.Info.Order.Should().BeGreaterThan(10, "le stock est migré après le socle et l'annuaire");
        database.Info.AdoptsLegacySchema.Should().BeFalse("aucune table du stock n'existait avant le découpage");

        await using var context = database.CreateContext();
        (await context.Database.GetAppliedMigrationsAsync()).Should().ContainSingle(m => m.EndsWith("_StockInitial", StringComparison.Ordinal));
        (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
        context.Database.HasPendingModelChanges().Should().BeFalse();
    }

    [Fact]
    public async Task Stock_context_maps_only_stk_tables_and_the_shared_journal()
    {
        await using var context = await _app.Get<IDbContextFactory<StockDbContext>>().CreateDbContextAsync();
        var tables = context.Model.GetEntityTypes().Select(e => e.GetTableName()!).Distinct().ToList();
        tables.Should().OnlyContain(t => t.StartsWith("stk_", StringComparison.Ordinal) || t == "sys_audit_entries");
        tables.Should().Contain("stk_locations");
    }

    [Fact]
    public async Task First_installation_creates_storage_workshop_and_shipping_locations()
    {
        await using var context = await _app.Get<IDbContextFactory<StockDbContext>>().CreateDbContextAsync();
        var locations = await context.Set<StockLocation>().OrderBy(l => l.SortOrder).ToListAsync();
        locations.Select(l => (l.Code, l.Kind)).Should().Equal(
            ("MAG", StockLocationKind.Storage), ("ATELIER", StockLocationKind.Workshop), ("EXPE", StockLocationKind.Shipping));
        locations.Should().OnlyContain(l => l.CreatedBy == "installation" && l.Version == 1);
    }

    [Fact]
    public async Task A_second_startup_does_not_duplicate_initial_locations()
    {
        await _app.Get<DatabaseInitializer>().InitializeAsync();

        await using var context = await _app.Get<IDbContextFactory<StockDbContext>>().CreateDbContextAsync();
        (await context.Set<StockLocation>().CountAsync()).Should().Be(3);
    }

    [Fact]
    public void Location_codes_are_normalized()
    {
        new StockLocation("  mag-a1 ", "Rack A1", StockLocationKind.Storage).Code.Should().Be("MAG-A1");
    }
}
