using System.Globalization;
using Barthazon.Annuaire;
using Barthazon.Commun;
using Barthazon.Commun.Donnees;
using Barthazon.Portail.Composants;
using Barthazon.Portail.Demo;
using Barthazon.Stock;

var builder = WebApplication.CreateBuilder(args);

// Service systemd sur la Raspberry Pi (sans effet en mode console).
builder.Host.UseSystemd();

// Culture française (dates, virgule décimale).
var culture = CultureInfo.GetCultureInfo("fr-FR");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Socle commun, puis chaque application (retirer une ligne retire l'application).
builder.Services.AjouterCommun(builder.Configuration, builder.Environment);
builder.Services.AjouterAnnuaire(builder.Configuration);
builder.Services.AjouterStock(builder.Configuration);

builder.Services.AddScoped<DonneesDemonstration>();

var app = builder.Build();

await app.Services.GetRequiredService<InitialiseurBase>().Initialiser();
if (app.Environment.IsDevelopment() && app.Configuration.GetValue("Barthazon:DonneesDemonstration", false))
{
    await using var portee = app.Services.CreateAsyncScope();
    await portee.ServiceProvider.GetRequiredService<DonneesDemonstration>().Creer();
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/erreur", createScopeForErrors: true);
}

app.Use(async (contexte, suite) =>
{
    // Pas d'affichage dans un cadre d'un autre site, pas de devinette de type de fichier.
    contexte.Response.Headers.XFrameOptions = "SAMEORIGIN";
    contexte.Response.Headers.XContentTypeOptions = "nosniff";
    await suite(contexte);
});
app.UseStatusCodePagesWithReExecute("/introuvable", createScopeForStatusCodePages: true);
app.UseAuthentication();
app.UtiliserCommun();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapCommun();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(Modules.Assemblages(app.Services));

await app.RunAsync();

/// <summary>Point d'entrée (visible des tests).</summary>
public partial class Program;
