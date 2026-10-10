using Barthazon.Shared.Kernel.Features;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.UI.Components;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor.Services;

namespace Barthazon.Shared.Tests.UI;

/// <summary>Fonctions activées selon le test.</summary>
internal sealed class FakeFeatures(params string[] enabled) : IFeatureFlags
{
    public bool IsEnabled(string featureName) => enabled.Contains(featureName);
}

public sealed class FeatureGateTests : BunitContext
{
    public FeatureGateTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [Fact]
    public void Disabled_feature_shows_greyed_placeholder_with_explanation()
    {
        Services.AddSingleton<IFeatureFlags>(new FakeFeatures());

        var cut = Render<FeatureGate>(p => p
            .Add(x => x.Feature, FeatureNames.Stock)
            .Add(x => x.Application, "Stock")
            .Add(x => x.Placeholder, (RenderFragment)(b => b.AddContent(0, "120 pcs")))
            .AddChildContent("contenu réel"));

        var zone = cut.Find(".bz-coming-soon");
        zone.GetAttribute("aria-disabled").Should().Be("true");
        zone.GetAttribute("aria-label").Should().Be("Bientôt disponible — arrivera avec l'application Stock");
        cut.Find(".bz-coming-soon-content").GetAttribute("aria-hidden").Should().Be("true");
        cut.Markup.Should().Contain("120 pcs").And.NotContain("contenu réel");
    }

    [Fact]
    public void Enabled_feature_shows_real_content()
    {
        Services.AddSingleton<IFeatureFlags>(new FakeFeatures(FeatureNames.Stock));

        var cut = Render<FeatureGate>(p => p
            .Add(x => x.Feature, FeatureNames.Stock)
            .Add(x => x.Placeholder, (RenderFragment)(b => b.AddContent(0, "factice")))
            .AddChildContent("contenu réel"));

        cut.Markup.Should().Contain("contenu réel").And.NotContain("factice");
        cut.FindAll(".bz-coming-soon").Should().BeEmpty();
    }

    [Fact]
    public void Inline_variant_is_a_compact_note()
    {
        Services.AddSingleton<IFeatureFlags>(new FakeFeatures());

        var cut = Render<FeatureGate>(p => p.Add(x => x.Feature, FeatureNames.Orders).Add(x => x.Inline, true).Add(x => x.Application, "Commandes"));

        cut.Find(".bz-coming-soon-inline").GetAttribute("title").Should().Contain("Commandes");
    }
}

public sealed class StatusChipTests : BunitContext
{
    [Theory]
    [InlineData(StatusTone.Success, "bz-tone-success")]
    [InlineData(StatusTone.Error, "bz-tone-error")]
    [InlineData(StatusTone.Disabled, "bz-tone-disabled")]
    public void Chip_shows_text_and_semantic_tone(StatusTone tone, string expectedClass)
    {
        var cut = Render<StatusChip>(p => p.Add(x => x.Text, "En vigueur").Add(x => x.Tone, tone));

        var chip = cut.Find("span.bz-status");
        chip.TextContent.Should().Be("En vigueur", "la couleur seule ne suffit pas");
        chip.ClassList.Should().Contain(expectedClass);
    }
}

public sealed class UnsavedChangesGuardTests : BunitContext
{
    public UnsavedChangesGuardTests()
    {
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddMudServices();
    }

    [Fact]
    public void Clean_form_lets_navigation_through()
    {
        Render<UnsavedChangesGuard>(p => p.Add(x => x.IsDirty, () => false));
        var navigation = Services.GetRequiredService<NavigationManager>();

        navigation.NavigateTo("/pieces");

        navigation.Uri.Should().EndWith("/pieces");
    }

    [Fact]
    public void Dirty_form_blocks_navigation_until_confirmed_and_shows_banner()
    {
        var cut = Render<UnsavedChangesGuard>(p => p.Add(x => x.IsDirty, () => true).Add(x => x.ShowBanner, true));
        var navigation = Services.GetRequiredService<NavigationManager>();
        var before = navigation.Uri;

        navigation.NavigateTo("/pieces");

        navigation.Uri.Should().Be(before, "la navigation attend la confirmation de l'utilisateur");
        cut.Markup.Should().Contain("Modifications non enregistrées");
    }
}

public sealed class FormStateTests
{
    private sealed class Model
    {
        public string Name { get; set; } = "A";
    }

    [Fact]
    public void Detects_changes_since_snapshot()
    {
        var state = new FormState();
        var model = new Model();
        state.Snapshot(model);

        state.IsDirty(model).Should().BeFalse();
        model.Name = "B";
        state.IsDirty(model).Should().BeTrue();
    }

    [Fact]
    public void Splits_field_and_general_errors()
    {
        var state = new FormState();

        state.Apply(Result.Failure(Error.Validation("Nom obligatoire", "Name"), Error.Conflict("Modifié par quelqu'un d'autre")));

        state.ErrorFor("Name").Should().Be("Nom obligatoire");
        state.GeneralErrors.Should().ContainSingle();
        state.HasConflict.Should().BeTrue();
    }
}
