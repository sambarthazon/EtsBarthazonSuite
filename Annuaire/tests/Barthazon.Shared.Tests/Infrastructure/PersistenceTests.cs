using System.Text.Json;
using Barthazon.Shared.Kernel.Auditing;
using Barthazon.Shared.Kernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Shared.Tests.Infrastructure;

public sealed class PersistenceTests : IAsyncLifetime
{
    private readonly SqliteTestDatabase _database = new();

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Adding_an_entity_fills_audit_fields_and_journal()
    {
        var widget = new Widget { Name = "Bague", Diameter = 12.5m };
        await using (var uow = _database.CreateUnitOfWork("chef"))
        {
            uow.Widgets.Add(widget);
            (await uow.SaveChangesAsync()).IsSuccess.Should().BeTrue();
        }

        await using var check = _database.CreateUnitOfWork();
        var stored = await check.Widgets.GetByIdAsync(widget.Id);
        stored!.CreatedBy.Should().Be("chef");
        stored.Version.Should().Be(1);
        stored.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1));
        var journal = await check.Db.AuditEntries.Where(e => e.EntityId == widget.Id).ToListAsync();
        journal.Should().ContainSingle(e => e.Action == AuditActions.Created && e.UserName == "chef" && e.EntityType == nameof(Widget));
    }

    [Fact]
    public async Task Modifying_records_old_and_new_values_and_increments_version()
    {
        var widget = new Widget { Name = "Axe" };
        await using (var uow = _database.CreateUnitOfWork())
        {
            uow.Widgets.Add(widget);
            await uow.SaveChangesAsync();
        }

        await using (var uow = _database.CreateUnitOfWork("secretariat"))
        {
            var loaded = await uow.Widgets.GetByIdAsync(widget.Id);
            loaded!.Name = "Axe long";
            (await uow.SaveChangesAsync()).IsSuccess.Should().BeTrue();
            loaded.Version.Should().Be(2);
            loaded.UpdatedBy.Should().Be("secretariat");
        }

        await using var check = _database.CreateUnitOfWork();
        var update = await check.Db.AuditEntries.SingleAsync(e => e.EntityId == widget.Id && e.Action == AuditActions.Updated);
        using var details = JsonDocument.Parse(update.Details!);
        details.RootElement.GetProperty("Name").GetProperty("Old").GetString().Should().Be("Axe");
        details.RootElement.GetProperty("Name").GetProperty("New").GetString().Should().Be("Axe long");
        details.RootElement.TryGetProperty("Version", out _).Should().BeFalse("les champs techniques ne sont pas tracés");
    }

    [Fact]
    public async Task Second_concurrent_modification_is_rejected_instead_of_overwriting()
    {
        var widget = new Widget { Name = "Bride" };
        await using (var uow = _database.CreateUnitOfWork())
        {
            uow.Widgets.Add(widget);
            await uow.SaveChangesAsync();
        }

        await using var first = _database.CreateUnitOfWork("a");
        await using var second = _database.CreateUnitOfWork("b");
        var a = await first.Widgets.GetByIdAsync(widget.Id);
        var b = await second.Widgets.GetByIdAsync(widget.Id);
        a!.Name = "Bride A";
        b!.Name = "Bride B";

        (await first.SaveChangesAsync()).IsSuccess.Should().BeTrue();
        var result = await second.SaveChangesAsync();

        result.IsFailure.Should().BeTrue();
        result.HasError(ErrorKind.Conflict).Should().BeTrue();
    }

    [Fact]
    public async Task Stale_expected_version_is_rejected()
    {
        var widget = new Widget { Name = "Support" };
        await using (var uow = _database.CreateUnitOfWork())
        {
            uow.Widgets.Add(widget);
            await uow.SaveChangesAsync();
        }

        await using (var uow = _database.CreateUnitOfWork())
        {
            var loaded = await uow.Widgets.GetByIdAsync(widget.Id);
            loaded!.Name = "Support modifié";
            await uow.SaveChangesAsync();
        }

        await using var stale = _database.CreateUnitOfWork();
        var entity = await stale.Widgets.GetByIdAsync(widget.Id);
        stale.Widgets.SetExpectedVersion(entity!, 1);
        entity!.Name = "Écrasement";
        (await stale.SaveChangesAsync()).HasError(ErrorKind.Conflict).Should().BeTrue();
    }

    [Fact]
    public async Task Unique_violation_returns_conflict()
    {
        await using var uow = _database.CreateUnitOfWork();
        uow.Widgets.Add(new Widget { Name = "Double" });
        uow.Widgets.Add(new Widget { Name = "Double" });

        (await uow.SaveChangesAsync()).HasError(ErrorKind.Conflict).Should().BeTrue();
    }

    [Fact]
    public async Task Archiving_is_journaled_as_archive()
    {
        var widget = new Widget { Name = "Ancien" };
        await using (var uow = _database.CreateUnitOfWork())
        {
            uow.Widgets.Add(widget);
            await uow.SaveChangesAsync();
        }

        await using (var uow = _database.CreateUnitOfWork())
        {
            (await uow.Widgets.GetByIdAsync(widget.Id))!.Archive();
            await uow.SaveChangesAsync();
        }

        await using var check = _database.CreateUnitOfWork();
        (await check.Db.AuditEntries.AnyAsync(e => e.EntityId == widget.Id && e.Action == AuditActions.Archived)).Should().BeTrue();
    }

    [Fact]
    public async Task Sqlite_can_sort_datetimeoffset_and_compare_decimals()
    {
        var now = DateTimeOffset.UtcNow;
        await using (var uow = _database.CreateUnitOfWork())
        {
            uow.Widgets.Add(new Widget { Name = "C", MeasuredAt = now.AddDays(2), Diameter = 100.25m });
            uow.Widgets.Add(new Widget { Name = "A", MeasuredAt = now.AddDays(-3), Diameter = 9.5m });
            uow.Widgets.Add(new Widget { Name = "B", MeasuredAt = new DateTimeOffset(now.AddHours(1).DateTime, TimeSpan.FromHours(2)), Diameter = 60m });
            await uow.SaveChangesAsync();
        }

        await using var check = _database.CreateUnitOfWork();
        (await check.OrderedByDateAsync()).Select(w => w.Name).Should().Equal("A", "B", "C");
        (await check.LargerThanAsync(10m)).Select(w => w.Name).Should().Equal("C", "B");
    }

    [Fact]
    public async Task Explicit_audit_note_is_saved_with_changes()
    {
        var id = Guid.NewGuid();
        await using (var uow = _database.CreateUnitOfWork("chef"))
        {
            uow.AddAuditNote("Part", id, AuditActions.PutInForce, "Indice C");
            await uow.SaveChangesAsync();
        }

        await using var check = _database.CreateUnitOfWork();
        var entry = await check.Db.AuditEntries.SingleAsync(e => e.EntityId == id);
        entry.Action.Should().Be(AuditActions.PutInForce);
        entry.Details.Should().Be("Indice C");
    }
}
