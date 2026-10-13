using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Shared.Infrastructure.Persistence;

/// <summary>
/// Partie « persistance » d'un module, implémentée par son projet <c>*.Infrastructure</c> : enregistre sa base
/// (<see cref="PersistenceServiceCollectionExtensions.AddModuleDatabase{TContext}"/>) ou ses mappings dans le socle
/// (<see cref="PersistenceServiceCollectionExtensions.AddSocleMappings"/>), ses repositories et ses données initiales.
/// </summary>
public interface IModulePersistence
{
    string Key { get; }

    void ConfigureServices(IServiceCollection services, IConfiguration configuration);
}
