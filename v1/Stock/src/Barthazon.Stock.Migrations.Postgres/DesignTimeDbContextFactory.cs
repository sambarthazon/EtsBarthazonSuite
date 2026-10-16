using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Stock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace Barthazon.Stock.Migrations.Postgres;

/// <summary>Utilisé par « dotnet ef » pour générer les migrations du stock (aucune connexion n'est ouverte).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<StockDbContext>
{
    public StockDbContext CreateDbContext(string[] args)
    {
        var options = new DatabaseOptions { Provider = DatabaseProviders.Postgres, ConnectionString = "Host=localhost;Database=barthazon;Username=barthazon;Password=design-time" };
        return new StockDbContext(DatabaseConnection.BuildOptions<StockDbContext>(options, StockDbContext.Info, null));
    }
}
