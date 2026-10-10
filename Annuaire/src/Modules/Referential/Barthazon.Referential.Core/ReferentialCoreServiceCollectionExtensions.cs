using Barthazon.Referential.Contracts;
using Barthazon.Referential.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Referential.Core;

public static class ReferentialCoreServiceCollectionExtensions
{
    /// <summary>Services applicatifs du référentiel et contrats exposés aux autres modules.</summary>
    public static IServiceCollection AddReferentialCore(this IServiceCollection services)
    {
        services.AddScoped<UsageCounter>();
        services.AddScoped<CustomerService>();
        services.AddScoped<ICustomerService>(sp => sp.GetRequiredService<CustomerService>());
        services.AddScoped<ICustomerLookup>(sp => sp.GetRequiredService<CustomerService>());
        services.AddScoped<MaterialService>();
        services.AddScoped<IMaterialService>(sp => sp.GetRequiredService<MaterialService>());
        services.AddScoped<IMaterialLookup>(sp => sp.GetRequiredService<MaterialService>());
        services.AddScoped<MachineService>();
        services.AddScoped<IMachineService>(sp => sp.GetRequiredService<MachineService>());
        services.AddScoped<IMachineLookup>(sp => sp.GetRequiredService<MachineService>());
        services.AddScoped<IUserService, UserService>();
        return services;
    }
}
