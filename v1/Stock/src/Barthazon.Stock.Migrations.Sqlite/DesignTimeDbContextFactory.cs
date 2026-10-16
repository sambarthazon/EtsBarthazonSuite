using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Stock.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace Barthazon.Stock.Migrations.Sqlite;

/// <summary>Utilisé par « dotnet ef » pour générer les migrations du stock (aucune connexion n'est ouverte).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<StockDbContext>
{
    public StockDbContext CreateDbContext(string[] args)
    {
        var options = new DatabaseOptions { Provider = DatabaseProviders.Sqlite, ConnectionString = "Data Source=design-time.db" };
        return new StockDbContext(DatabaseConnection.BuildOptions<StockDbContext>(options, StockDbContext.Info, null));
    }
}
