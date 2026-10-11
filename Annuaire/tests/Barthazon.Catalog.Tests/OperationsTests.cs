using Barthazon.Catalog.Core.Services;
using Barthazon.Referential.Core.Services;
using Barthazon.Shared.Infrastructure.Administration;
using Barthazon.Shared.Infrastructure.Backup;
using Barthazon.Shared.Infrastructure.DataTransfer;
using Barthazon.Shared.Infrastructure.Hosting;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Barthazon.Catalog.Tests;

/// <summary>Exploitation : sauvegarde, export / import complet, contrôle de santé.</summary>
public sealed class OperationsTests : IAsyncLifetime
{
    private TestApplication _app = default!;
    private CatalogTestData _data = default!;

    public async Task InitializeAsync()
    {
        _app = await TestApplication.CreateAsync();
        _data = new CatalogTestData(_app);
        await _data.SeedAsync();
        await _data.CreatePartAsync("P-0700", "Arbre", customerReference: "AR-7");
        _app.User.As(UserRole.Administrator, "admin");
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    /// <summary>La sauvegarde intégrée (VACUUM INTO) ne concerne que SQLite, même quand les tests tournent sur PostgreSQL.</summary>
    private static async Task<TestApplication> SqliteApplicationAsync()
    {
        var app = await TestApplication.CreateAsync(settings: new() { ["Database:Provider"] = "Sqlite" });
        var data = new CatalogTestData(app);
        await data.SeedAsync();
        await data.CreatePartAsync("P-0700", "Arbre", customerReference: "AR-7");
        app.User.As(UserRole.Administrator, "admin");
        return app;
    }

    [Fact]
    public async Task Backup_creates_a_consistent_copy_and_records_status()
    {
        await using var app = await SqliteApplicationAsync();
        var result = await app.Get<AdministrationService>().BackupNowAsync();

        result.IsSuccess.Should().BeTrue(result.FirstError?.Message);
        var path = Path.Combine(app.Root, "backups", result.Value.Name);
        File.Exists(path).Should().BeTrue();
        await using (var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False"))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM cat_parts";
            (await command.ExecuteScalarAsync()).Should().Be(1L);
        }

        var status = (await app.Get<AdministrationService>().GetStatusAsync()).Value;
        status.Backup.LastSuccessAt.Should().NotBeNull();
        status.BackupOverdue.Should().BeFalse();
    }

    [Fact]
    public async Task Restoring_a_backup_file_brings_back_the_saved_state()
    {
        // Procédure de docs/sauvegarde-restauration.md : remplacer le fichier de la base par une sauvegarde.
        await using var app = await SqliteApplicationAsync();
        var backup = (await app.Get<AdministrationService>().BackupNowAsync()).Value;
        app.User.As(UserRole.Editor, "editeur");
        var customerId = (await app.Get<IPartService>().GetByReferenceAsync("P-0700")).Value.CustomerId;
        await app.Get<IPartService>().CreateAsync(new PartCreateInput { InternalReference = "P-0701", Designation = "Créée après la sauvegarde", CustomerId = customerId, FirstIndex = "A" });

        var restoredFile = Path.Combine(app.Root, "restauree.db");
        File.Copy(Path.Combine(app.Root, "backups", backup.Name), restoredFile);
        await using var restored = await TestApplication.CreateAsync(settings: new()
        {
            ["Database:Provider"] = "Sqlite",
            ["Database:ConnectionString"] = $"Data Source={restoredFile};Pooling=False",
        });

        (await restored.Get<IPartService>().GetByReferenceAsync("P-0700")).IsSuccess.Should().BeTrue();
        (await restored.Get<IPartService>().GetByReferenceAsync("P-0701")).IsSuccess.Should().BeFalse("la pièce a été créée après la sauvegarde");
        (await restored.Get<IPartService>().GetHistoryAsync((await restored.Get<IPartService>().GetByReferenceAsync("P-0700")).Value.Id)).Value.Should().NotBeEmpty();
    }

    [Fact]
    public async Task Old_backups_are_removed_by_retention()
    {
        await using var app = await SqliteApplicationAsync();
        var directory = Path.Combine(app.Root, "backups");
        Directory.CreateDirectory(directory);
        var old = Path.Combine(directory, "barthazon-20000101-000000.db");
        await File.WriteAllTextAsync(old, "ancien");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-40));

        await app.Get<BackupService>().RunAsync();

        File.Exists(old).Should().BeFalse();
        app.Get<BackupService>().ListFiles().Should().ContainSingle();
    }

    [Fact]
    public async Task Without_backup_the_status_is_overdue()
    {
        await using var app = await SqliteApplicationAsync();
        var status = (await app.Get<AdministrationService>().GetStatusAsync()).Value;

        status.BackupOverdue.Should().BeTrue();
    }

    [Fact]
    public async Task Full_json_export_can_be_imported_into_a_fresh_database()
    {
        var part = (await _data.Parts.GetByReferenceAsync("P-0700")).Value;
        var revision = (await _data.Parts.GetRevisionAsync(part.Id, part.CurrentRevisionId!.Value)).Value;
        var input = RevisionInput.From(revision);
        input.Operations.Add(new OperationInput { Number = 10, MachineId = _data.MachineId });
        input.Characteristics.Add(new CharacteristicInput { Name = "Filetage", Value = "M8" });
        await _data.Revisions.UpdateAsync(part.Id, revision.Id, input);
        await _app.Get<IUserService>().CreateFirstAdministratorAsync(new UserInput { UserName = "admin", DisplayName = "Admin", Password = "motdepasse" });
        var export = (await _app.Get<AdministrationService>().ExportAllAsync()).Value;

        await using var target = await TestApplication.CreateAsync();
        target.User.As(UserRole.Administrator, "admin");
        using var stream = new MemoryStream(export.Content);
        var report = await target.Get<AdministrationService>().ImportAllAsync(stream);

        report.IsSuccess.Should().BeTrue(report.FirstError?.Message);
        report.Value.RowsByTable["cat_parts"].Should().Be(1);
        var imported = (await target.Get<IPartService>().GetByReferenceAsync("P-0700")).Value;
        imported.Id.Should().Be(part.Id);
        imported.CustomerCode.Should().Be("DUPONT");
        var importedRevision = (await target.Get<IPartService>().GetRevisionAsync(imported.Id, imported.CurrentRevisionId!.Value)).Value;
        importedRevision.Operations.Single().MachineName.Should().Be("Tour CN Okuma");
        importedRevision.Characteristics.Single().Value.Should().Be("M8");
        (await target.Get<IPartService>().SearchAsync(new PartSearchRequest { Query = "dupont ar7" })).Value.TotalCount.Should().Be(1);
        (await target.Get<IUserService>().AuthenticateAsync("admin", "motdepasse")).IsSuccess.Should().BeTrue();
        (await target.Get<IPartService>().GetHistoryAsync(imported.Id)).Value.Should().NotBeEmpty("le journal est aussi transféré");
    }

    [Fact]
    public async Task Json_export_from_sqlite_is_imported_into_the_configured_database()
    {
        // Scénario « passage à PostgreSQL » : avec BZ_TEST_POSTGRES, la cible est une base PostgreSQL.
        await using var source = await SqliteApplicationAsync();
        await source.Get<IUserService>().CreateFirstAdministratorAsync(new UserInput { UserName = "admin", DisplayName = "Admin", Password = "motdepasse" });
        var export = (await source.Get<AdministrationService>().ExportAllAsync()).Value;

        await using var target = await TestApplication.CreateAsync();
        target.User.As(UserRole.Administrator, "admin");
        using var stream = new MemoryStream(export.Content);
        var report = await target.Get<AdministrationService>().ImportAllAsync(stream);

        report.IsSuccess.Should().BeTrue(report.FirstError?.Message);
        var imported = (await target.Get<IPartService>().GetByReferenceAsync("P-0700")).Value;
        imported.CustomerCode.Should().Be("DUPONT");
        (await target.Get<IPartService>().SearchAsync(new PartSearchRequest { Query = "arbre ar7" })).Value.TotalCount.Should().Be(1);
        (await target.Get<IUserService>().AuthenticateAsync("admin", "motdepasse")).IsSuccess.Should().BeTrue();

        // La base importée reste modifiable (jetons de concurrence, identifiants).
        target.User.As(UserRole.Editor, "editeur");
        var created = await target.Get<IPartService>().CreateAsync(new PartCreateInput { InternalReference = "P-0701", Designation = "Bague", CustomerId = imported.CustomerId, FirstIndex = "A" });
        created.IsSuccess.Should().BeTrue(created.FirstError?.Message);
    }

    [Fact]
    public async Task Import_rejects_files_that_are_not_exports()
    {
        using var stream = new MemoryStream("{\"hello\":1}"u8.ToArray());

        var result = await _app.Get<AdministrationService>().ImportAllAsync(stream);

        result.HasError(ErrorKind.Validation).Should().BeTrue();
        (await _data.Parts.SearchAsync(new PartSearchRequest())).Value.TotalCount.Should().Be(1, "rien n'est effacé");
    }

    [Fact]
    public async Task Administration_is_reserved_to_administrators()
    {
        _app.User.As(UserRole.Editor);

        (await _app.Get<AdministrationService>().ExportAllAsync()).HasError(ErrorKind.Forbidden).Should().BeTrue();
        (await _app.Get<AdministrationService>().BackupNowAsync()).HasError(ErrorKind.Forbidden).Should().BeTrue();
    }

    [Fact]
    public async Task Health_checks_report_database_and_files()
    {
        var context = new HealthCheckContext();

        (await _app.Get<DatabaseHealthCheck>().CheckHealthAsync(context)).Status.Should().Be(HealthStatus.Healthy);
        (await new FileStorageHealthCheck(_app.Get<Barthazon.Shared.Infrastructure.Files.FileStorageOptions>()).CheckHealthAsync(context)).Status.Should().Be(HealthStatus.Healthy);
    }

    [Fact]
    public void Tables_are_ordered_by_dependencies()
    {
        using var context = _app.Get<Microsoft.EntityFrameworkCore.IDbContextFactory<Barthazon.Shared.Infrastructure.Persistence.AppDbContext>>().CreateDbContext();
        var order = DataTransferService.OrderedEntityTypes(context.Model).Select(t => t.GetTableName()).ToList();

        order.IndexOf("cat_parts").Should().BeLessThan(order.IndexOf("cat_part_revisions"));
        order.IndexOf("cat_part_revisions").Should().BeLessThan(order.IndexOf("cat_routing_operations"));
        order.IndexOf("cat_tags").Should().BeLessThan(order.IndexOf("cat_part_tags"));
    }
}
