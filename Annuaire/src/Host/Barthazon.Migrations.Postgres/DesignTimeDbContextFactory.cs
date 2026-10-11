using Barthazon.Catalog.Infrastructure;
using Barthazon.Referential.Infrastructure;
using Barthazon.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace Barthazon.Migrations.Postgres;

/// <summary>
/// Utilisé par « dotnet ef » pour générer les migrations PostgreSQL de tous les modules
/// (aucune connexion n'est ouverte pour générer une migration).
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DatabaseOptions { Provider = DatabaseProviders.Postgres, ConnectionString = "Host=localhost;Database=barthazon;Username=barthazon;Password=design-time" };
        return new AppDbContextFactory(options, [new ReferentialPersistence(), new CatalogPersistence()], TimeProvider.System).CreateDbContext();
    }
}
