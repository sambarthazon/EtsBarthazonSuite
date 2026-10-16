using System.Globalization;
using Barthazon.Catalog.Contracts;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Shared.UI.Services;
using Barthazon.Stock.Core;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Services;
using Barthazon.Stock.UI.Components;
using Barthazon.Testing;
using Bunit;
using Microsoft.Extensions.DependencyInjection;
using MudBlazor;
using MudBlazor.Services;

namespace Barthazon.Stock.Tests;

/// <summary>Interface (bUnit) : dialogue de déclaration et pipeline (section 15).</summary>
public sealed class BatchUiTests : BunitContext, IAsyncLifetime
{
    private StockTestApp _app = default!;

    public async Task InitializeAsync()
    {
        CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
        _app = await StockTestApp.CreateAsync();
        JSInterop.Mode = JSRuntimeMode.Loose;
        Services.AddSharedUi();
        Services.AddSingleton<ICurrentUser>(_app.User);
        Services.AddSingleton(_app.Get<IBatchService>());
        Services.AddSingleton(_app.Get<ILocationService>());
        Services.AddSingleton(_app.Get<StockOptions>());
        Services.AddSingleton<TimeProvider>(_app.Clock);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await _app.DisposeAsync();
        await DisposeAsync();
    }

    [Fact]
    public async Task The_declaration_dialog_is_prefilled_with_everything_waiting_and_asks_a_reason_for_scrap()
    {
        var part = _app.Catalog.AddPart("P-0001", operations: [OperationType.Turning, OperationType.Milling]);
        var batch = (await _app.Get<IBatchService>().LaunchAsync(new LaunchInput { PartId = part.Id, Quantity = 28, WithoutConsumption = true })).Value;
        var provider = Render<MudDialogProvider>();
        var dialogs = Services.GetRequiredService<IDialogService>();

        await provider.InvokeAsync(() => dialogs.ShowAsync<DeclareDialog>("Op 10", new DialogParameters<DeclareDialog>
        {
            { d => d.BatchId, batch.Id },
            { d => d.Step, batch.Steps[0] },
        }));

        provider.WaitForAssertion(() => provider.Markup.Should().Contain("28 pièce(s) attendent cette opération"));
        provider.FindAll("input").Select(i => i.GetAttribute("value")).Should().Contain("28", "la quantité bonne est pré-remplie avec tout ce qui attend");
        provider.Markup.Should().NotContain("Motif du rebut");

        var scrap = provider.FindAll("input").Where(i => i.GetAttribute("inputmode") == "numeric").ElementAt(1);
        scrap.Input("2");
        provider.WaitForAssertion(() => provider.Markup.Should().Contain("Motif du rebut"));
    }

    [Fact]
    public void The_pipeline_greys_operations_outside_a_batch_routing_and_lists_operations_on_small_screens()
    {
        var pipeline = new PartPipeline(
            [new PipelineColumn(10, OperationType.Cutting), new PipelineColumn(20, OperationType.Turning), new PipelineColumn(35, OperationType.Deburring)],
            [
                new PipelineRow(Guid.NewGuid(), "L2026-014", "B", new Dictionary<int, int> { [10] = 0, [20] = 8 }, new Dictionary<int, int> { [10] = 1, [20] = 2 }, 20),
                new PipelineRow(Guid.NewGuid(), "L2026-015", "C", new Dictionary<int, int> { [10] = 3, [20] = 0, [35] = 2 }, new Dictionary<int, int> { [10] = 1, [20] = 2, [35] = 3 }, 0),
            ],
            new Dictionary<int, int> { [10] = 3, [20] = 8, [35] = 2 },
            20);

        var cut = Render<PipelineView>(p => p.Add(x => x.Pipeline, pipeline));

        cut.FindAll("td.bz-na").Should().ContainSingle("L2026-014 n'a pas d'opération 35");
        cut.Markup.Should().Contain("8 en attente de l'op 20 — Tournage").And.Contain("Total : 13 en cours, 20 finie(s)");
    }
}
