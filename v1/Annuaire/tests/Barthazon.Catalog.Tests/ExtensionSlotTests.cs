using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Services;
using Barthazon.Catalog.UI.Components;
using Barthazon.Catalog.UI.Pages;
using Barthazon.Shared.Kernel.Features;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Shared.UI.Services;
using Barthazon.Testing;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;

namespace Barthazon.Catalog.Tests;

/// <summary>
/// Emplacements de l'annuaire (section 9.1 de la spécification du stock), avec une fausse application :
/// l'onglet Stock et les cartes du tableau de bord s'activent sans que l'annuaire connaisse cette application.
/// </summary>
public sealed class ExtensionSlotTests : BunitContext, IAsyncLifetime
{
    private TestApplication _app = default!;

    private sealed class Features(params string[] enabled) : IFeatureFlags
    {
        public bool IsEnabled(string featureName) => enabled.Contains(featureName);
    }

    private sealed class FakeStockPanel : ComponentBase
    {
        [Parameter] public Guid PartId { get; set; }

        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, $"panneau stock {PartId}");
    }

    private sealed class FakeDashboardCard : ComponentBase
    {
        protected override void BuildRenderTree(RenderTreeBuilder builder) => builder.AddContent(0, "carte stock");
    }

    public async Task InitializeAsync()
    {
        _app = await CatalogTestApp.CreateAsync();
        await new CatalogTestData(_app).SeedAsync();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSharedUi();
        Services.AddSingleton(_app.Get<IPartStockInfoProvider>());
        Services.AddSingleton(_app.Get<IDashboardService>());
        Services.AddSingleton(_app.Get<IFavoriteService>());
        Services.AddSingleton(_app.Get<IPartService>());
        Services.AddSingleton<ICurrentUser>(_app.User);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _app.DisposeAsync();
        await DisposeAsync();
    }

    [Fact]
    public void Stock_tab_shows_the_stock_application_panel_when_active()
    {
        var partId = Guid.NewGuid();
        Services.AddSingleton<IFeatureFlags>(new Features(FeatureNames.Stock));
        Services.AddUiExtension(new UiExtension(CatalogUiSlots.PartStockTab, typeof(FakeStockPanel), "Stock"));

        var cut = Render<PartStockTab>(p => p.Add(x => x.PartId, partId));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain($"panneau stock {partId}"));
    }

    [Fact]
    public void Stock_tab_keeps_the_summary_when_no_application_fills_the_slot()
    {
        Services.AddSingleton<IFeatureFlags>(new Features(FeatureNames.Stock));

        var cut = Render<PartStockTab>(p => p.Add(x => x.PartId, Guid.NewGuid()));

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Non disponible"));
    }

    [Fact]
    public void Stock_tab_stays_greyed_when_the_feature_is_off_even_with_an_extension()
    {
        Services.AddSingleton<IFeatureFlags>(new Features());
        Services.AddUiExtension(new UiExtension(CatalogUiSlots.PartStockTab, typeof(FakeStockPanel), "Stock"));

        var cut = Render<PartStockTab>(p => p.Add(x => x.PartId, Guid.NewGuid()));

        cut.Markup.Should().NotContain("panneau stock").And.Contain("bz-coming-soon");
    }

    [Fact]
    public void Dashboard_replaces_the_threshold_card_with_the_stock_cards()
    {
        Services.AddSingleton<IFeatureFlags>(new Features(FeatureNames.Stock));
        Services.AddUiExtension(new UiExtension(CatalogUiSlots.DashboardCards, typeof(FakeDashboardCard), "Stock"));

        var cut = Render<Dashboard>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("carte stock").And.NotContain("Pièces sous le seuil"));
    }

    [Fact]
    public void Dashboard_keeps_the_greyed_threshold_card_without_stock()
    {
        Services.AddSingleton<IFeatureFlags>(new Features());
        Services.AddUiExtension(new UiExtension(CatalogUiSlots.DashboardCards, typeof(FakeDashboardCard), "Stock"));

        var cut = Render<Dashboard>();

        cut.WaitForAssertion(() => cut.Markup.Should().Contain("Pièces sous le seuil").And.NotContain("carte stock"));
    }
}
