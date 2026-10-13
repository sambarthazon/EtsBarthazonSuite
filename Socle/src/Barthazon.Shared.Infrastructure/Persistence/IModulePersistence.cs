using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Shared.Infrastructure.Persistence;

/// <summary>
/// Partie « persistance » d'un module : assemblage contenant ses mappings EF Core
/// (<c>IEntityTypeConfiguration</c>) et enregistrement de ses repositories.
/// Implémentée par le projet <c>*.Infrastructure</c> de chaque module.
/// </summary>
public interface IModulePersistence
{
    string Key { get; }

    Assembly MappingsAssembly { get; }

    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
}
