using System.Globalization;
using Barthazon.Portal.Components;
using Barthazon.Portal.Hosting;
using Barthazon.Shared.Infrastructure.Features;
using Barthazon.Shared.Kernel.Features;
using Barthazon.Shared.Kernel.Modules;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Shared.Kernel.Settings;
using Barthazon.Shared.UI.Services;

var builder = WebApplication.CreateBuilder(args);

// Culture française (dates, virgule décimale).
var culture = CultureInfo.GetCultureInfo(builder.Configuration["App:Culture"] ?? "fr-FR");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<BrandingOptions>(builder.Configuration.GetSection(BrandingOptions.SectionName));
builder.Services.AddSharedUi();

// Modules : chaque module enregistre ses propres services.
var modules = PortalModules.All;
builder.Services.AddSingleton<IModuleCatalog>(new ModuleCatalog(modules));
foreach (var module in modules)
{
    module.ConfigureServices(builder.Services, builder.Configuration);
}

builder.Services.AddScoped<CurrentUserAccessor>();
builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<CurrentUserAccessor>());
builder.Services.AddSingleton<IFeatureFlags, ConfigurationFeatureFlags>();
builder.Services.AddSingleton<IApplicationSettingsStore, ConfigurationSettingsStore>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
