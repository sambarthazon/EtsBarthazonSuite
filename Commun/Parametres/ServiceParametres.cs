using Barthazon.Commun.Donnees;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Commun.Parametres;

/// <summary>Paramètres enregistrés en base (table com_Parametre). L'apparence a son propre service : <see cref="ServiceTheme"/>.</summary>
public sealed class ServiceParametres(IDbContextFactory<CommunDbContext> fabrique)
{
    public async Task<string?> Lire(string cle)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        return (await db.Parametres.AsNoTracking().FirstOrDefaultAsync(p => p.Cle == cle))?.Valeur;
    }

    public async Task Ecrire(string cle, string valeur)
    {
        await using var db = await fabrique.CreateDbContextAsync();
        var parametre = await db.Parametres.FirstOrDefaultAsync(p => p.Cle == cle);
        if (parametre is null)
        {
            db.Parametres.Add(new Parametre { Cle = cle, Valeur = valeur });
        }
        else
        {
            parametre.Valeur = valeur;
        }

        await db.SaveChangesAsync();
    }
}
