using System.Globalization;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Shared.UI.Services;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Services;
using Barthazon.Stock.UI.Pages;
using Barthazon.Testing;
using Bunit;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Stock.Tests;

/// <summary>Interface (bUnit) : page de comptage d'un inventaire.</summary>
public sealed class InventoryUiTests : BunitContext, IAsyncLifetime
{
    private StockTestApp _app = default!;

    public async Task InitializeAsync()
    {
        _app = await StockTestApp.CreateAsync();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSharedUi();
        Services.AddSingleton<ICurrentUser>(_app.User);
        Services.AddSingleton(_app.Get<IInventoryService>());
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _app.DisposeAsync();
        await DisposeAsync();
    }

    [Fact]
    public async Task Counting_a_line_as_expected_records_it_and_differences_are_shown()
    {
        // La culture fixée dans InitializeAsync ne revient pas dans le contexte d'exécution du test.
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
        var data = new StockTestData(_app);
        var bar = await data.CreateItemAsync("A-0001", "Rond C45 Ø50", StockUnit.Meter, StockItemKind.RawMaterial);
        var blank = await data.CreateItemAsync("A-0002", "Débit Ø40");
        await data.ReceiveAsync(bar.Id, 12.5m, "MAG");
        await data.ReceiveAsync(blank.Id, 30, "MAG");
        var inventory = (await _app.Get<IInventoryService>().OpenAsync(new OpenInventoryInput { Title = "Magasin" })).Value;

        var cut = Render<InventoryPage>(p => p.Add(x => x.Number, inventory.Number));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Rond C45 Ø50").And.Contain("0 / 2 comptée(s)"));
        cut.Markup.Should().Contain("12,5 m");
        cut.FindAll("button[aria-label='Conforme à l\\'attendu']").First().Click();
        cut.WaitForAssertion(() => cut.Markup.Should().Contain("1 / 2 comptée(s)"));

        var blankLine = (await _app.Get<IInventoryService>().GetByNumberAsync(inventory.Number)).Value.Lines.Single(l => l.SubjectCode == "A-0002");
        await _app.Get<IInventoryService>().CountAsync(inventory.Id, [new InventoryCountInput(blankLine.Id, 28)]);
        var refreshed = Render<InventoryPage>(p => p.Add(x => x.Number, inventory.Number));
        refreshed.WaitForAssertion(() => refreshed.Markup.Should().Contain("1 écart(s)").And.Contain("−2 u"));
    }
}
