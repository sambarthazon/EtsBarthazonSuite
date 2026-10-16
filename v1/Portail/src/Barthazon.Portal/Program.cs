using System.Globalization;
using Barthazon.Portal.Components;
using Barthazon.Portal.Endpoints;
using Barthazon.Portal.Hosting;
using Barthazon.Portal.Hosting.Demo;
using Barthazon.Shared.Infrastructure;
using Barthazon.Shared.Infrastructure.Backup;
using Barthazon.Shared.Infrastructure.Hosting;
using Barthazon.Shared.Infrastructure.Persistence;
using Barthazon.Shared.Infrastructure.Security;
using Barthazon.Shared.Kernel.Features;
using Barthazon.Shared.Kernel.Modules;
using Barthazon.Shared.UI.Services;
using Barthazon.Stock.Core;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // Service Windows : le dossier de travail est C:\Windows\System32, on se base sur le dossier de l'application.
    ContentRootPath = Microsoft.Extensions.Hosting.WindowsServices.WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : null,
});

// Exécution en service Windows (PC du bureau) ou systemd (Raspberry Pi) ; sans effet en mode console.
builder.Host.UseWindowsService(options => options.ServiceName = "Barthazon");
builder.Host.UseSystemd();

// Journaux : console + fichiers tournants (un par jour, 14 jours par défaut).
var logDirectory = Path.GetFullPath(builder.Configuration["Logging:Directory"] ?? "logs", builder.Environment.ContentRootPath);
var retainedDays = builder.Configuration.GetValue("Logging:RetainedDays", 14);
builder.Host.UseSerilog((context, configuration) => configuration
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
    .WriteTo.File(Path.Combine(logDirectory, "barthazon-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: retainedDays, formatProvider: CultureInfo.InvariantCulture));

// Validation de la configuration : en cas d'erreur, arrêt immédiat avec un message clair.
var report = ConfigurationValidator.Validate(builder.Configuration, builder.Environment.ContentRootPath);
if (builder.Configuration.GetValue("Features:" + FeatureNames.Stock, false))
{
    report = report with { Errors = [.. report.Errors, .. StockOptions.Validate(builder.Configuration)] };
}
if (!report.IsValid)
{
    var message = "Configuration invalide — l'application ne peut pas démarrer :" + Environment.NewLine
        + string.Join(Environment.NewLine, report.Errors.Select(e => "  - " + e));
    Console.Error.WriteLine(message);

    // En service Windows la console n'est visible par personne : le message est aussi écrit dans un fichier.
    foreach (var directory in new[] { logDirectory, builder.Environment.ContentRootPath })
    {
        try
        {
            Directory.CreateDirectory(directory);
            File.AppendAllText(
                Path.Combine(directory, "barthazon-erreur-demarrage.log"),
                $"[{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss}] {message}{Environment.NewLine}");
            break;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Dossier inaccessible : on essaie le suivant.
        }
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
var modules = PortalModules.Create(builder.Configuration);
builder.Services.AddSingleton<IModuleCatalog>(new ModuleCatalog(modules));
foreach (var module in modules)
{
    module.ConfigureServices(builder.Services, builder.Configuration);
}

// Comptes locaux, cookie, utilisateur courant.
var security = builder.Configuration.GetSection(SecurityOptions.SectionName).Get<SecurityOptions>() ?? new SecurityOptions();
var databaseOptions = builder.Configuration.GetSection(DatabaseOptions.SectionName).Get<DatabaseOptions>() ?? new DatabaseOptions();
var sqliteFile = DatabaseConnection.GetSqliteFilePath(databaseOptions);
var keysDirectory = Path.GetFullPath(
    security.KeysDirectory
        ?? (sqliteFile is null ? Path.Combine("App_Data", "keys") : Path.Combine(Path.GetDirectoryName(sqliteFile)!, "keys")),
    builder.Environment.ContentRootPath);
builder.Services.AddPortalAuthentication(security, keysDirectory);

builder.Services.AddSingleton<DemoDataSeeder>();
builder.Services.AddHostedService<BackupScheduler>();

var app = builder.Build();

await app.Services.GetRequiredService<DatabaseInitializer>().InitializeAsync();
if (app.Configuration.GetValue("Seed:DemoData", false))
{
    await app.Services.GetRequiredService<DemoDataSeeder>().SeedAsync();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
}

// En-têtes de protection : pas d'affichage dans un cadre d'un autre site (clic détourné), pas de devinette de type.
app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers.XFrameOptions = "SAMEORIGIN";
    headers.XContentTypeOptions = "nosniff";
    headers["Referrer-Policy"] = "same-origin";
    await next();
});
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
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.ToDictionary(e => e.Key, e => new { status = e.Value.Status.ToString(), description = e.Value.Description }),
        });
    },
}).AllowAnonymous();
app.MapAccountEndpoints();
app.MapFileEndpoints();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(modules.Select(m => m.UiAssembly).OfType<System.Reflection.Assembly>().Distinct().ToArray());

try
{
    await app.RunAsync();
    return 0;
}
finally
{
    await Log.CloseAndFlushAsync();
}
