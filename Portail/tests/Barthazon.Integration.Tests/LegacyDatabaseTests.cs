using Barthazon.Catalog.Core.Services;
using Barthazon.Catalog.Infrastructure.Persistence;
using Barthazon.Referential.Core.Services;
using Barthazon.Shared.Infrastructure.Administration;
using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Integration.Tests;

/// <summary>
/// Reprise d'une base créée avant le découpage en un contexte par application (section 6.6 de la spécification du stock).
/// La fixture <c>Fixtures/barthazon-v1.db</c> a été générée par la version 1 (une seule table d'historique, migration Initial commune).
/// </summary>
public sealed class LegacyDatabaseTests
{
    private const string FixtureDb = "barthazon-v1.db";

    private static Dictionary<string, string?> SqliteAt(string path) => new()
    {
        ["Database:Provider"] = "Sqlite",
        ["Database:ConnectionString"] = $"Data Source={path};Pooling=False",
    };

    [Fact]
    public async Task Version_1_database_is_adopted_and_all_its_data_stays_readable_and_modifiable()
    {
        var path = IntegrationTestApp.CopyFixture(FixtureDb);

        await using var app = await IntegrationTestApp.CreateAsync(settings: SqliteAt(path));

        var parts = (await app.Get<IPartService>().SearchAsync(new PartSearchRequest { PageSize = 100 })).Value;
        parts.TotalCount.Should().Be(15);
        (await app.Get<IUserService>().AuthenticateAsync("admin", "Barthazon2026")).IsSuccess.Should().BeTrue("les comptes sont repris");
        var part = (await app.Get<IPartService>().GetByReferenceAsync("P-0001")).Value;
        (await app.Get<IPartService>().GetHistoryAsync(part.Id)).Value.Should().NotBeEmpty("le journal est repris");

        app.User.As(UserRole.Editor, "editeur");
        var input = PartInput.From(part);
        input.Designation = "Arbre modifié après reprise";
        (await app.Get<IPartService>().UpdateAsync(part.Id, input)).IsSuccess.Should().BeTrue();
        var created = await app.Get<IPartService>().CreateAsync(new PartCreateInput { InternalReference = "P-0999", Designation = "Nouvelle", CustomerId = part.CustomerId, FirstIndex = "A" });
        created.IsSuccess.Should().BeTrue(created.FirstError?.Message);

        HistoryTables(path).Should().Contain(["__ef_migrations_socle", "__ef_migrations_catalog", DatabaseInitializer.LegacyHistoryArchiveTable])
            .And.NotContain(DatabaseInitializer.LegacyHistoryTable);
    }

    [Fact]
    public async Task Adopted_database_and_new_database_have_the_same_schema()
    {
        var legacy = IntegrationTestApp.CopyFixture(FixtureDb);
        await using (await IntegrationTestApp.CreateAsync(settings: SqliteAt(legacy)))
        {
        }

        var fresh = Path.Combine(Path.GetDirectoryName(legacy)!, "neuve.db");
        await using (await IntegrationTestApp.CreateAsync(settings: SqliteAt(fresh)))
        {
        }

        NormalizedSchema(legacy).Should().Equal(NormalizedSchema(fresh));
    }

    [Fact]
    public async Task Each_context_has_no_pending_model_changes()
    {
        await using var app = await IntegrationTestApp.CreateAsync(settings: SqliteAt(Path.Combine(Path.GetTempPath(), $"bz-{Guid.NewGuid():N}.db")));
        foreach (var database in app.Services.GetServices<ModuleDatabase>())
        {
            await using var context = database.CreateContext();
            context.Database.HasPendingModelChanges().Should().BeFalse($"le modèle de « {database.Info.Key} » doit correspondre à ses migrations");
        }
    }

    [Fact]
    public async Task A_second_startup_changes_nothing()
    {
        var path = IntegrationTestApp.CopyFixture(FixtureDb);
        await using var app = await IntegrationTestApp.CreateAsync(settings: SqliteAt(path));
        var before = NormalizedSchema(path);
        var auditBefore = Count(path, "sys_audit_entries");

        var initializer = app.Get<DatabaseInitializer>();
        (await initializer.AdoptLegacyDatabaseAsync()).Should().BeFalse("la reprise a déjà été faite");
        await initializer.InitializeAsync();

        NormalizedSchema(path).Should().Equal(before);
        Count(path, "sys_audit_entries").Should().Be(auditBefore);
        Count(path, "__ef_migrations_socle").Should().Be(1);
        Count(path, "__ef_migrations_catalog").Should().Be(1);
    }

    [Fact]
    public async Task Version_1_json_export_is_imported_into_a_new_database()
    {
        await using var app = await IntegrationTestApp.CreateAsync();
        app.User.As(UserRole.Administrator, "admin");

        await using var file = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "barthazon-v1.json"));
        var report = await app.Get<AdministrationService>().ImportAllAsync(file);

        report.IsSuccess.Should().BeTrue(report.FirstError?.Message);
        report.Value.RowsByTable["cat_parts"].Should().Be(15);
        (await app.Get<IPartService>().SearchAsync(new PartSearchRequest { Query = "arbre" })).Value.TotalCount.Should().BePositive();
        (await app.Get<IUserService>().AuthenticateAsync("admin", "Barthazon2026")).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Simulated_legacy_database_is_adopted_on_the_configured_provider()
    {
        // Variante indépendante du fournisseur (SQLite ou PostgreSQL avec BZ_TEST_POSTGRES) : base neuve ramenée à
        // l'état « ancienne génération » (une seule table d'historique avec la migration Initial commune).
        await using var app = await IntegrationTestApp.CreateAsync();
        var databases = app.Services.GetServices<ModuleDatabase>().OrderBy(d => d.Info.Order).ToList();
        await using (var context = databases[0].CreateContext())
        {
            var legacyId = context.Database.IsNpgsql() ? "20261011022423_Initial" : "20261011022402_Initial";
#pragma warning disable EF1002 // Noms de tables constants (test).
            // Seules les applications de la version 1 (socle, annuaire) ont un historique à reprendre.
            foreach (var database in databases.Where(d => d.Info.AdoptsLegacySchema))
            {
                await context.Database.ExecuteSqlRawAsync($"DROP TABLE \"{database.Info.HistoryTable}\"");
            }

            await context.Database.ExecuteSqlRawAsync("CREATE TABLE \"__EFMigrationsHistory\" (\"MigrationId\" varchar(150) NOT NULL PRIMARY KEY, \"ProductVersion\" varchar(32) NOT NULL)");
            await context.Database.ExecuteSqlRawAsync($"INSERT INTO \"__EFMigrationsHistory\" VALUES ('{legacyId}', '10.0.0')");
#pragma warning restore EF1002
        }

        var initializer = app.Get<DatabaseInitializer>();
        await initializer.InitializeAsync();

        foreach (var database in databases)
        {
            await using var context = database.CreateContext();
            (await context.Database.GetPendingMigrationsAsync()).Should().BeEmpty();
            if (database.Info.AdoptsLegacySchema)
            {
                (await context.Database.GetAppliedMigrationsAsync()).Should().ContainSingle("seule la migration initiale est inscrite à la reprise");
            }
        }

        (await app.Get<IPartService>().SearchAsync(new PartSearchRequest())).IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task Each_context_maps_only_its_own_tables()
    {
        await using var app = await IntegrationTestApp.CreateAsync();
        foreach (var database in app.Services.GetServices<ModuleDatabase>())
        {
            await using var context = database.CreateContext();
            foreach (var table in context.Model.GetEntityTypes().Select(t => t.GetTableName()).OfType<string>())
            {
                (database.Info.TablePrefixes.Any(p => table.StartsWith(p, StringComparison.Ordinal)) || table == "sys_audit_entries")
                    .Should().BeTrue($"le contexte « {database.Info.Key} » ne doit mapper que ses tables (trouvé : {table})");
            }
        }
    }

    private static List<string> HistoryTables(string path) =>
        Query(path, "SELECT name FROM sqlite_master WHERE type = 'table' AND name LIKE '\\_\\_%' ESCAPE '\\'");

    /// <summary>Schéma comparable : tables, index et vues de l'application (hors tables techniques de migration).</summary>
    private static List<string> NormalizedSchema(string path) =>
        Query(path, "SELECT type || ' ' || name || ' ' || IFNULL(sql, '') FROM sqlite_master WHERE name NOT LIKE '\\_\\_%' ESCAPE '\\' AND name NOT LIKE 'sqlite_%' ORDER BY type, name")
            .Select(s => string.Join(' ', s.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)))
            .ToList();

    private static long Count(string path, string table)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
#pragma warning disable CA2100 // Nom de table constant (test).
        command.CommandText = $"SELECT COUNT(*) FROM \"{table}\"";
#pragma warning restore CA2100
        return (long)command.ExecuteScalar()!;
    }

    private static List<string> Query(string path, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        using var reader = command.ExecuteReader();
        var result = new List<string>();
        while (reader.Read())
        {
            result.Add(reader.GetString(0));
        }

        return result;
    }
}
