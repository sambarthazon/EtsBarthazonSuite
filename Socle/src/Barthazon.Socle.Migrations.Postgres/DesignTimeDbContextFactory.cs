using Barthazon.Referential.Infrastructure;
using Barthazon.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Design;

namespace Barthazon.Socle.Migrations.Postgres;

/// <summary>Utilisé par « dotnet ef » pour générer les migrations du socle (aucune connexion n'est ouverte).</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<SocleDbContext>
{
    public SocleDbContext CreateDbContext(string[] args)
    {
        var options = new DatabaseOptions { Provider = DatabaseProviders.Postgres, ConnectionString = "Host=localhost;Database=barthazon;Username=barthazon;Password=design-time" };
        return new SocleDbContext(DatabaseConnection.BuildOptions<SocleDbContext>(options, SocleDbContext.Info, null), [typeof(ReferentialPersistence).Assembly]);
    }
}
