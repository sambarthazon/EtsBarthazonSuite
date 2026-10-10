using Barthazon.Catalog.Infrastructure;
using Barthazon.Referential.Infrastructure;
using Barthazon.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace Barthazon.Migrations.Sqlite;

/// <summary>
/// Utilisé par « dotnet ef » pour générer les migrations SQLite de tous les modules.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DatabaseOptions { Provider = DatabaseProviders.Sqlite, ConnectionString = "Data Source=design-time.db" };
        return new AppDbContextFactory(options, [new ReferentialPersistence(), new CatalogPersistence()], TimeProvider.System).CreateDbContext();
    }
}
