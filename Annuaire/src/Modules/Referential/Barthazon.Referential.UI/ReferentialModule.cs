using System.Reflection;
using Barthazon.Referential.UI.Resources;
using Barthazon.Shared.Kernel.Modules;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;

namespace Barthazon.Referential.UI;

/// <summary>Module « Référentiel » : données partagées par toutes les applications (clients, matières, machines, utilisateurs).</summary>
public sealed class ReferentialModule : IModule
{
    public string Key => "referential";

    public string DisplayName => ReferentialTexts.ModuleName;

    public string Description => ReferentialTexts.ModuleDescription;

    public string Icon => Icons.Material.Filled.Storage;

    public string EntryRoute => "clients";

    public IReadOnlyList<string> RoutePrefixes { get; } = ["/clients", "/referentiels"];

    public ModuleState State => ModuleState.Available;

    public int Order => 0;

    public bool IsApplication => false;

    public IReadOnlyList<ModuleMenuItem> MenuItems { get; } =
    [
        new(ReferentialTexts.MenuCustomers, "clients", Icons.Material.Filled.Business),
        new(ReferentialTexts.MenuReferentials, "referentiels", Icons.Material.Filled.Tune, ShowInBottomBar: false),
    ];

    public Assembly? UiAssembly => typeof(ReferentialModule).Assembly;

    public void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
    }
}
