using System.Reflection;
using Barthazon.Referential.Core.Persistence;
using Barthazon.Referential.Infrastructure.Persistence;
using Barthazon.Referential.Infrastructure.Security;
using Barthazon.Referential.Infrastructure.Seeding;
using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Kernel.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Referential.Infrastructure;

/// <summary>Persistance du module Référentiel : mappings EF Core (tables ref_*) et repositories.</summary>
public sealed class ReferentialPersistence : IModulePersistence
{
    public string Key => "referential";

    public Assembly MappingsAssembly => typeof(ReferentialPersistence).Assembly;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IUnitOfWorkFactory<IReferentialUnitOfWork>, ReferentialUnitOfWorkFactory>();
        services.AddSingleton<IDataSeeder, ReferentialSeeder>();
        services.AddSingleton<Core.Services.IPasswordHasher, AspNetPasswordHasher>();
    }
}
