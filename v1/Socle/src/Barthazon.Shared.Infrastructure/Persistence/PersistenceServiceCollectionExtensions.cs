using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Shared.Infrastructure.Persistence;

/// <summary>Assemblage de mappings ajouté au contexte du socle (ex. référentiel).</summary>
public sealed record SocleMappingsAssembly(Assembly Assembly);

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Déclare la base d'une application : contexte, fabrique (<see cref="IDbContextFactory{TContext}"/>),
    /// migrations et place dans l'ordre d'initialisation.
    /// </summary>
    public static IServiceCollection AddModuleDatabase<TContext>(
        this IServiceCollection services,
        ModuleDatabaseInfo info,
        Func<IServiceProvider, DbContextOptions<TContext>, TContext> create)
        where TContext : ModuleDbContext
    {
        ArgumentNullException.ThrowIfNull(info);
        ArgumentNullException.ThrowIfNull(create);
        services.AddSingleton(sp => new ModuleDatabase<TContext>(info, sp.GetRequiredService<DatabaseOptions>(), options => create(sp, options)));
        services.AddSingleton<ModuleDatabase>(sp => sp.GetRequiredService<ModuleDatabase<TContext>>());
        services.AddSingleton<IDbContextFactory<TContext>>(sp => sp.GetRequiredService<ModuleDatabase<TContext>>());
        return services;
    }

    /// <summary>Ajoute les mappings d'un assemblage au contexte du socle (tables partagées par toutes les applications).</summary>
    public static IServiceCollection AddSocleMappings(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        services.AddSingleton(new SocleMappingsAssembly(assembly));
        return services;
    }
}
