using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Barthazon.Shared.UI.Services;

public static class SharedUiServiceCollectionExtensions
{
    /// <summary>Enregistre MudBlazor et les services communs d'interface.</summary>
    public static IServiceCollection AddSharedUi(this IServiceCollection services)
    {
        services.AddMudServices(config =>
        {
            config.SnackbarConfiguration.PositionClass = Defaults.Classes.Position.BottomCenter;
            config.SnackbarConfiguration.PreventDuplicates = true;
            config.SnackbarConfiguration.VisibleStateDuration = 4000;
            config.SnackbarConfiguration.ShowCloseIcon = true;
        });
        services.AddScoped<BrowserInterop>();
        services.AddScoped<UiExtensionRegistry>();
        services.AddScoped<Notifier>();
        return services;
    }

    /// <summary>Déclare un composant à insérer dans un emplacement d'interface.</summary>
    public static IServiceCollection AddUiExtension(this IServiceCollection services, UiExtension extension)
    {
        services.AddSingleton(extension);
        return services;
    }
}
