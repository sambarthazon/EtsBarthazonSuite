using System.Reflection;
using Barthazon.Shared.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Referential.Infrastructure;

/// <summary>Persistance du module Referential : mappings EF Core (tables préfixées) et repositories.</summary>
public sealed class ReferentialPersistence : IModulePersistence
{
    public string Key => "referential";

    public Assembly MappingsAssembly => typeof(ReferentialPersistence).Assembly;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
