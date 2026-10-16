using System.Reflection;
using Barthazon.Commun.Modules;

namespace Barthazon.Portail.Composants;

/// <summary>Assemblages contenant des pages : le socle commun et chaque application chargée.</summary>
internal static class Modules
{
    public static Assembly[] Assemblages(IServiceProvider services) =>
        [
            typeof(CatalogueModules).Assembly,
            .. services.GetRequiredService<CatalogueModules>().Modules.Select(m => m.Assemblage).Distinct(),
        ];
}
