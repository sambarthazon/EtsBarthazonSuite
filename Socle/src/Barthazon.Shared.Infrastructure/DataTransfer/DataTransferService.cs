using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Barthazon.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Barthazon.Shared.Infrastructure.DataTransfer;

/// <summary>Bilan d'un import complet.</summary>
public sealed record DataTransferReport(IReadOnlyDictionary<string, int> RowsByTable)
{
    public int TotalRows => RowsByTable.Values.Sum();
}

/// <summary>
/// Export / import complet des données en JSON, indépendant du fournisseur de base :
/// sert à transférer le contenu de SQLite vers PostgreSQL. Parcourt le modèle EF Core de chaque application
/// (dans l'ordre des migrations : socle d'abord ; tables dans l'ordre des dépendances), sans connaître les entités.
/// <para>
/// Format version 2 : mêmes tables par nom que la version 1 (contexte unique), plus la liste des applications.
/// L'import accepte les deux versions : les tables sont reconnues par leur nom.
/// </para>
/// </summary>
public sealed class DataTransferService(IEnumerable<ModuleDatabase> databases, DatabaseOptions database, TimeProvider timeProvider)
{
    public const string Format = "barthazon-export";
    public const int FormatVersion = 2;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    private static readonly MethodInfo SetMethod = typeof(DbContext).GetMethods()
        .Single(m => m.Name == nameof(DbContext.Set) && m.IsGenericMethodDefinition && m.GetParameters().Length == 0);

    private IReadOnlyList<ModuleDatabase> Ordered => databases.OrderBy(d => d.Info.Order).ThenBy(d => d.Info.Key, StringComparer.Ordinal).ToList();

    public async Task<byte[]> ExportAsync(CancellationToken cancellationToken = default)
    {
        using var output = new MemoryStream();
        await using (var writer = new Utf8JsonWriter(output))
        {
            writer.WriteStartObject();
            writer.WriteString("format", Format);
            writer.WriteNumber("version", FormatVersion);
            writer.WriteString("exportedAt", timeProvider.GetUtcNow());
            writer.WriteString("provider", database.Provider);
            writer.WriteStartArray("applications");
            foreach (var module in Ordered)
            {
                writer.WriteStringValue(module.Info.Key);
            }

            writer.WriteEndArray();
            writer.WriteStartObject("tables");
            foreach (var module in Ordered)
            {
                await using var context = module.CreateContext();
                foreach (var entityType in OwnedEntityTypes(context.Model, module.Info))
                {
                    writer.WriteStartArray(entityType.GetTableName()!);
                    foreach (var entity in await LoadAllAsync(context, entityType, cancellationToken))
                    {
                        var entry = context.Entry(entity);
                        writer.WriteStartObject();
                        foreach (var property in entityType.GetProperties())
                        {
                            writer.WritePropertyName(property.Name);
                            JsonSerializer.Serialize(writer, entry.Property(property.Name).CurrentValue, property.ClrType, JsonOptions);
                        }

                        writer.WriteEndObject();
                    }

                    writer.WriteEndArray();
                }
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return output.ToArray();
    }

    /// <summary>
    /// Remplace TOUTES les données de la base par celles du fichier (base de destination fraîchement installée).
    /// Une seule transaction pour toutes les applications : en cas d'erreur, rien n'est modifié.
    /// </summary>
    public async Task<DataTransferReport> ImportAsync(Stream content, CancellationToken cancellationToken = default)
    {
        using var document = await JsonDocument.ParseAsync(content, cancellationToken: cancellationToken);
        var root = document.RootElement;
        if (!root.TryGetProperty("format", out var format) || format.GetString() != Format
            || !root.TryGetProperty("version", out var version) || version.GetInt32() is < 1 or > FormatVersion
            || !root.TryGetProperty("tables", out var tables))
        {
            throw new InvalidDataException("format");
        }

        await using var connection = DatabaseConnection.CreateConnection(database);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // 1. Vider les tables : applications dans l'ordre inverse, dépendants d'abord.
        foreach (var module in Enumerable.Reverse(Ordered))
        {
            await using var context = module.CreateContext(connection);
            await context.Database.UseTransactionAsync(transaction, cancellationToken);
            context.SuppressAudit = true;
            foreach (var entityType in Enumerable.Reverse(OwnedEntityTypes(context.Model, module.Info).ToList()))
            {
                foreach (var entity in await LoadAllAsync(context, entityType, cancellationToken, tracking: true))
                {
                    context.Remove(entity);
                }

                await context.SaveChangesAsync(cancellationToken);
                context.ChangeTracker.Clear();
            }
        }

        // 2. Insérer : socle d'abord, tables principales avant leurs dépendants.
        var report = new Dictionary<string, int>();
        foreach (var module in Ordered)
        {
            await using var context = module.CreateContext(connection);
            await context.Database.UseTransactionAsync(transaction, cancellationToken);
            context.SuppressAudit = true;
            foreach (var entityType in OwnedEntityTypes(context.Model, module.Info))
            {
                var table = entityType.GetTableName()!;
                if (!tables.TryGetProperty(table, out var rows))
                {
                    continue;
                }

                var count = 0;
                foreach (var row in rows.EnumerateArray())
                {
                    var entity = Activator.CreateInstance(entityType.ClrType, nonPublic: true)!;
                    var entry = context.Entry(entity);
                    foreach (var property in entityType.GetProperties())
                    {
                        if (row.TryGetProperty(property.Name, out var value))
                        {
                            entry.Property(property.Name).CurrentValue = value.Deserialize(property.ClrType, JsonOptions);
                        }
                    }

                    entry.State = EntityState.Added;
                    count++;
                }

                await context.SaveChangesAsync(cancellationToken);
                context.ChangeTracker.Clear();
                report[table] = count;
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return new DataTransferReport(report);
    }

    /// <summary>
    /// Tables d'une application (d'après ses préfixes) : le journal commun, mappé par chaque contexte,
    /// n'appartient qu'au socle.
    /// </summary>
    public static IEnumerable<IEntityType> OwnedEntityTypes(IModel model, ModuleDatabaseInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return OrderedEntityTypes(model).Where(t => info.TablePrefixes.Any(p => t.GetTableName()!.StartsWith(p, StringComparison.Ordinal)));
    }

    /// <summary>Types d'entités dans l'ordre des dépendances (une table principale avant ses dépendants).</summary>
    public static IEnumerable<IEntityType> OrderedEntityTypes(IModel model)
    {
        var types = model.GetEntityTypes().Where(t => !t.IsOwned() && t.GetTableName() is not null).OrderBy(t => t.GetTableName(), StringComparer.Ordinal).ToList();
        var done = new HashSet<IEntityType>();
        var result = new List<IEntityType>();
        void Visit(IEntityType type, HashSet<IEntityType> visiting)
        {
            if (done.Contains(type) || !visiting.Add(type))
            {
                return;
            }

            foreach (var principal in type.GetForeignKeys().Select(fk => fk.PrincipalEntityType).Where(p => p != type))
            {
                Visit(principal, visiting);
            }

            done.Add(type);
            result.Add(type);
        }

        foreach (var type in types)
        {
            Visit(type, []);
        }

        return result;
    }

    private static async Task<List<object>> LoadAllAsync(ModuleDbContext context, IEntityType entityType, CancellationToken cancellationToken, bool tracking = false)
    {
        var set = (IQueryable<object>)SetMethod.MakeGenericMethod(entityType.ClrType).Invoke(context, null)!;
        var query = tracking ? set : set.AsNoTracking();
        return await query.ToListAsync(cancellationToken);
    }
}
