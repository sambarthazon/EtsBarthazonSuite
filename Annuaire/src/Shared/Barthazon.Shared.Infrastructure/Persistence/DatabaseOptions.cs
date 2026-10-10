namespace Barthazon.Shared.Infrastructure.Persistence;

/// <summary>Fournisseurs de base de données pris en charge.</summary>
public static class DatabaseProviders
{
    public const string Sqlite = "Sqlite";
    public const string Postgres = "Postgres";
}

/// <summary>Section de configuration « Database ».</summary>
public sealed class DatabaseOptions
{
    public const string SectionName = "Database";

    public string Provider { get; set; } = DatabaseProviders.Sqlite;

    public string ConnectionString { get; set; } = string.Empty;

    public bool ApplyMigrationsOnStartup { get; set; } = true;

    public bool IsSqlite => string.Equals(Provider, DatabaseProviders.Sqlite, StringComparison.OrdinalIgnoreCase);

    public bool IsPostgres => string.Equals(Provider, DatabaseProviders.Postgres, StringComparison.OrdinalIgnoreCase);
}
