using Barthazon.Catalog.Infrastructure.Persistence;
using Barthazon.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace Barthazon.Catalog.Migrations.Postgres;

/// <summary>Utilisé par « dotnet ef » pour générer les migrations de l'annuaire (aucune connexion n'est ouverte).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DatabaseOptions { Provider = DatabaseProviders.Postgres, ConnectionString = "Host=localhost;Database=barthazon;Username=barthazon;Password=design-time" };
        return new CatalogDbContext(DatabaseConnection.BuildOptions<CatalogDbContext>(options, CatalogDbContext.Info, null));
    }
}
