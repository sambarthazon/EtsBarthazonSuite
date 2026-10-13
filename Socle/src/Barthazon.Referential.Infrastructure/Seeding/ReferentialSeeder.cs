using Barthazon.Referential.Contracts;
using Barthazon.Referential.Core.Domain;
using Barthazon.Shared.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Barthazon.Referential.Infrastructure.Seeding;

/// <summary>À la première installation : matières courantes en mécanique générale.</summary>
internal sealed class ReferentialSeeder(IDbContextFactory<SocleDbContext> contextFactory) : IDataSeeder
{
    private static readonly (string Designation, string Grade, MaterialFamily Family, decimal Density)[] CommonMaterials =
    [
        ("Acier C45", "1.0503", MaterialFamily.Steel, 7.85m),
        ("Acier S235", "1.0038", MaterialFamily.Steel, 7.85m),
        ("Acier S355", "1.0570", MaterialFamily.Steel, 7.85m),
        ("Acier 42CrMo4", "1.7225", MaterialFamily.Steel, 7.85m),
        ("Acier 11SMnPb30 (décolletage)", "1.0718", MaterialFamily.Steel, 7.85m),
        ("Acier 35NiCrMo16", "1.2766", MaterialFamily.Steel, 7.85m),
        ("Inox 304L", "1.4307", MaterialFamily.Stainless, 7.90m),
        ("Inox 316L", "1.4404", MaterialFamily.Stainless, 8.00m),
        ("Inox 303", "1.4305", MaterialFamily.Stainless, 7.90m),
        ("Inox 17-4PH", "1.4542", MaterialFamily.Stainless, 7.80m),
        ("Aluminium 2017A", "AW-2017A", MaterialFamily.Aluminium, 2.79m),
        ("Aluminium 6082", "AW-6082", MaterialFamily.Aluminium, 2.70m),
        ("Aluminium 7075", "AW-7075", MaterialFamily.Aluminium, 2.81m),
        ("Aluminium 5083", "AW-5083", MaterialFamily.Aluminium, 2.66m),
        ("Laiton de décolletage", "CW614N", MaterialFamily.CopperAlloy, 8.47m),
        ("Bronze", "CuSn12", MaterialFamily.CopperAlloy, 8.80m),
        ("Cuivre", "Cu-ETP", MaterialFamily.CopperAlloy, 8.94m),
        ("Fonte grise", "EN-GJL-250", MaterialFamily.CastIron, 7.20m),
        ("Fonte GS", "EN-GJS-400-15", MaterialFamily.CastIron, 7.10m),
        ("Polyacétal", "POM-C", MaterialFamily.Plastic, 1.41m),
        ("Polyamide", "PA6", MaterialFamily.Plastic, 1.14m),
        ("Polyéthylène", "PE-HD 1000", MaterialFamily.Plastic, 0.93m),
        ("PTFE", "PTFE", MaterialFamily.Plastic, 2.17m),
        ("Titane TA6V", "3.7165", MaterialFamily.Titanium, 4.43m),
    ];

    public int Order => 10;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        await using var context = await contextFactory.CreateDbContextAsync(cancellationToken);
        if (await context.Set<Material>().AnyAsync(cancellationToken))
        {
            return;
        }

        context.AuditUserName = "installation";
        foreach (var (designation, grade, family, density) in CommonMaterials)
        {
            context.Add(new Material(designation, grade, family) { DensityKgDm3 = density });
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
