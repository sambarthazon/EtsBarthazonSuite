using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Kernel.Domain;
using Barthazon.Shared.Kernel.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Barthazon.Shared.Tests.Infrastructure;

/// <summary>Entité de test pour éprouver le socle de données sur une vraie base SQLite.</summary>
public sealed class Widget : AuditableEntity
{
    public string Name { get; set; } = string.Empty;

    public decimal Diameter { get; set; }

    public DateTimeOffset MeasuredAt { get; set; }
}

internal sealed class WidgetConfiguration : IEntityTypeConfiguration<Widget>
{
    public void Configure(EntityTypeBuilder<Widget> builder)
    {
        builder.ToTable("test_widgets");
        builder.HasKey(w => w.Id);
        builder.HasIndex(w => w.Name).IsUnique();
    }
}

/// <summary>Contexte de test : une application fictive avec sa table.</summary>
public sealed class WidgetDbContext(DbContextOptions<WidgetDbContext> options) : ModuleDbContext(options)
{
    public static ModuleDatabaseInfo Info { get; } = new("widgets", 10, "__ef_migrations_widgets", "none", "none", ["test_"]);

    protected override bool OwnsSharedTables => true;

    protected override void ApplyMappings(ModelBuilder modelBuilder) => modelBuilder.ApplyConfiguration(new WidgetConfiguration());
}

internal sealed class WidgetUnitOfWork(ModuleDbContext context, string? user) : EfUnitOfWork(context, user)
{
    public IRepository<Widget> Widgets { get; } = new EfRepository<Widget>(context);

    public Task<List<Widget>> OrderedByDateAsync() => Context.Set<Widget>().OrderBy(w => w.MeasuredAt).ToListAsync();

    public Task<List<Widget>> LargerThanAsync(decimal diameter) =>
        Context.Set<Widget>().Where(w => w.Diameter > diameter).OrderByDescending(w => w.Diameter).ToListAsync();

    public ModuleDbContext Db => Context;
}

/// <summary>Base SQLite temporaire (fichier), supprimée à la fin du test.</summary>
public sealed class SqliteTestDatabase : IAsyncDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "bz-tests-" + Guid.NewGuid().ToString("N"));

    public SqliteTestDatabase()
    {
        Directory.CreateDirectory(_directory);
        var options = new DatabaseOptions { ConnectionString = $"Data Source={Path.Combine(_directory, "test.db")};Pooling=False" };
        Factory = new ModuleDatabase<WidgetDbContext>(WidgetDbContext.Info, options, o => new WidgetDbContext(o));
        using var context = Factory.CreateDbContext();
        context.Database.EnsureCreated();
    }

    public ModuleDatabase<WidgetDbContext> Factory { get; }

    internal WidgetUnitOfWork CreateUnitOfWork(string? user = "testeur") => new(Factory.CreateDbContext(), user);

    public ValueTask DisposeAsync()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
        }

        return ValueTask.CompletedTask;
    }
}
