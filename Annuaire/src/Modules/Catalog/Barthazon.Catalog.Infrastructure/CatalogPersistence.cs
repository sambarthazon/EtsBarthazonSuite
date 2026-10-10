using System.Reflection;
using Barthazon.Shared.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Catalog.Infrastructure;

/// <summary>Persistance du module Catalog : mappings EF Core (tables préfixées) et repositories.</summary>
public sealed class CatalogPersistence : IModulePersistence
{
    public string Key => "catalog";

    public Assembly MappingsAssembly => typeof(CatalogPersistence).Assembly;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
