using System.Globalization;
using Barthazon.Portal.Components;
using Barthazon.Portal.Endpoints;
using Barthazon.Portal.Hosting;
using Barthazon.Shared.Infrastructure;
using Barthazon.Shared.Infrastructure.Hosting;
using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Infrastructure.Security;
using Barthazon.Shared.Kernel.Modules;
using Barthazon.Shared.UI.Services;

var builder = WebApplication.CreateBuilder(args);

// Validation de la configuration : en cas d'erreur, arrêt immédiat avec un message clair.
var report = ConfigurationValidator.Validate(builder.Configuration, builder.Environment.ContentRootPath);
if (!report.IsValid)
{
    Console.Error.WriteLine("Configuration invalide — l'application ne peut pas démarrer :");
    foreach (var error in report.Errors)
    {
        Console.Error.WriteLine("  - " + error);
    }

    return 1;
}

// Culture française (dates, virgule décimale).
var culture = CultureInfo.GetCultureInfo(builder.Configuration["App:Culture"] ?? "fr-FR");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.Configure<BrandingOptions>(builder.Configuration.GetSection(BrandingOptions.SectionName));
builder.Services.AddSharedUi();
builder.Services.AddSingleton(new ConfigurationWarnings(report.Warnings));

// Socle de données + persistance des modules.
builder.Services.AddSharedInfrastructure(builder.Configuration, builder.Environment.ContentRootPath, PortalModules.Persistence);

// Modules : chaque module enregistre ses propres services.
var modules = PortalModules.All;
builder.Services.AddSingleton<IModuleCatalog>(new ModuleCatalog(modules));
foreach (var module in modules)
{
    module.ConfigureServices(builder.Services, builder.Configuration);
}

// Comptes locaux, cookie, utilisateur courant.
var security = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();
builder.Services.AddPortalAuthentication(security);

var app = builder.Build();

await app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.Use(async (context, next) =>
{
    // Utilisateur courant pour les services appelés pendant la requête HTTP (prérendu, téléchargements).
    context.RequestServices.GetRequiredService<CurrentUserAccessor>().SetUser(context.User);
    await next();
});
app.UseAuthorization();
app.UseFirstRunRedirect();
app.UseAntiforgery();

app.MapStaticAssets().AllowAnonymous();
app.MapAccountEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(modules.Select(m => m.UiAssembly).OfType<System.Reflection.Assembly>().Distinct().ToArray());

await app.RunAsync();
return 0;
