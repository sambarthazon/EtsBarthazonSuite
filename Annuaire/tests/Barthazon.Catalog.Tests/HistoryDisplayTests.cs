using System.Globalization;
using Barthazon.Catalog.Core.Services;
using Barthazon.Catalog.UI.Components;
using Barthazon.Testing;

namespace Barthazon.Catalog.Tests;

/// <summary>Lisibilité du journal : valeurs traduites, champs techniques absents.</summary>
public sealed class HistoryDisplayTests
{
    [Fact]
    public void Statuses_and_dates_are_shown_in_french()
    {
        var previous = CultureInfo.CurrentUICulture;
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
        try
        {
            CatalogLabels.ValueLabel("Status", "InForce").Should().Be("En vigueur");
            CatalogLabels.ValueLabel("Status", "Active").Should().Be("Active");
            CatalogLabels.ValueLabel("EffectiveDate", "2026-10-11").Should().Be("11/10/2026");
            CatalogLabels.ValueLabel("Designation", "Bague 2026-10-11").Should().Be("Bague 2026-10-11");
        }
        finally
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = previous;
        }
    }

    [Fact]
    public async Task Technical_fields_do_not_appear_in_the_history()
    {
        await using var app = await CatalogTestApp.CreateAsync();
        var data = new CatalogTestData(app);
        await data.SeedAsync();
        var part = await data.CreatePartAsync("P-0900", "Axe", inForce: false);

        await data.Revisions.PutInForceAsync(part.Id, part.Revisions.Single().Id);

        var history = (await data.Parts.GetHistoryAsync(part.Id)).Value;
        history.SelectMany(h => h.Changes).Select(c => c.Field)
            .Should().NotContain(["RevisionsChangedAt", "CurrentRevisionId", "StorageKey", "Sha256"]);
    }
}
