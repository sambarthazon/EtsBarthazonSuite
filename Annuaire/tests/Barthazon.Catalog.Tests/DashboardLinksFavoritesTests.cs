using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Persistence;
using Barthazon.Catalog.Core.Services;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Testing;

namespace Barthazon.Catalog.Tests;

/// <summary>Tableau de bord, favoris et liens entre pièces (J9).</summary>
public sealed class DashboardLinksFavoritesTests
{
    [Fact]
    public async Task Dashboard_counts_parts_by_status_and_customer_and_lists_quality_issues()
    {
        await using var app = await TestApplication.CreateAsync();
        var data = new CatalogTestData(app);
        await data.SeedAsync();
        await data.CreatePartAsync("P-0001", "Axe");
        await data.CreatePartAsync("P-0002", "Bague", inForce: false);
        var archived = await data.CreatePartAsync("P-0003", "Bride");
        await data.Parts.SetArchivedAsync(archived.Id, true);

        var summary = (await app.Get<IDashboardService>().GetSummaryAsync()).Value;

        summary.TotalParts.Should().Be(2, "les pièces archivées ne sont pas comptées");
        summary.CountByStatus.Values.Sum().Should().Be(2);
        summary.CountByCustomer.Should().ContainSingle().Which.Should().Match<CustomerPartCount>(c => c.Code == "DUPONT" && c.Count == 2);
        summary.RecentlyModified.Select(p => p.InternalReference).Should().BeEquivalentTo("P-0001", "P-0002");
        summary.QualityIssues[PartQualityIssue.NoInForceRevision].Should().Be(1);
        summary.QualityIssues[PartQualityIssue.PendingDraft].Should().Be(1);
        summary.QualityIssues[PartQualityIssue.NoDrawing].Should().Be(2);
        summary.QualityIssues[PartQualityIssue.NoMaterial].Should().Be(0);
    }

    [Fact]
    public async Task Parts_remembered_by_the_browser_are_returned_in_the_same_order()
    {
        await using var app = await TestApplication.CreateAsync();
        var data = new CatalogTestData(app);
        await data.SeedAsync();
        await data.CreatePartAsync("P-0001", "Axe");
        await data.CreatePartAsync("P-0002", "Bague");

        var parts = await app.Get<IDashboardService>().GetByReferencesAsync(["p-0002", "INCONNUE", "P-0001"]);

        parts.Select(p => p.InternalReference).Should().Equal("P-0002", "P-0001");
    }

    [Fact]
    public async Task A_logged_in_reader_manages_personal_favorites()
    {
        await using var app = await TestApplication.CreateAsync();
        var data = new CatalogTestData(app);
        await data.SeedAsync();
        var axe = await data.CreatePartAsync("P-0001", "Axe");
        var bague = await data.CreatePartAsync("P-0002", "Bague");
        app.User.As(UserRole.Reader);
        var favorites = app.Get<IFavoriteService>();

        favorites.IsAvailable.Should().BeTrue();
        (await favorites.SetAsync(axe.Id, true)).IsSuccess.Should().BeTrue();
        (await favorites.SetAsync(bague.Id, true)).IsSuccess.Should().BeTrue();
        (await favorites.SetAsync(bague.Id, true)).IsSuccess.Should().BeTrue("ajouter deux fois ne fait rien");
        (await favorites.IsFavoriteAsync(axe.Id)).Should().BeTrue();
        (await favorites.ListAsync()).Should().HaveCount(2);

        (await favorites.SetAsync(axe.Id, false)).IsSuccess.Should().BeTrue();
        (await favorites.ListAsync()).Select(p => p.InternalReference).Should().Equal("P-0002");
    }

    [Fact]
    public async Task An_anonymous_visitor_has_no_server_favorites()
    {
        await using var app = await TestApplication.CreateAsync();
        var data = new CatalogTestData(app);
        await data.SeedAsync();
        var axe = await data.CreatePartAsync("P-0001", "Axe");
        app.User.SignOut();
        var favorites = app.Get<IFavoriteService>();

        favorites.IsAvailable.Should().BeFalse();
        (await favorites.SetAsync(axe.Id, true)).Errors.Should().ContainSingle(e => e.Kind == ErrorKind.Unauthenticated);
        (await favorites.ListAsync()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_link_is_shown_with_its_inverse_label_on_the_other_part()
    {
        await using var app = await TestApplication.CreateAsync();
        var data = new CatalogTestData(app);
        await data.SeedAsync();
        var assembly = await data.CreatePartAsync("P-0001", "Ensemble");
        var component = await data.CreatePartAsync("P-0002", "Bague");
        var links = app.Get<IPartLinkService>();

        // Depuis l'ensemble : « Se compose de » la bague (lien inverse saisi depuis la cible).
        var added = await links.AddAsync(new PartLinkInput { PartId = assembly.Id, OtherPartId = component.Id, Type = PartLinkType.ComponentOf, IsInverse = true, Quantity = 2 });
        added.IsSuccess.Should().BeTrue();

        var fromComponent = (await links.ListAsync(component.Id)).Value.Should().ContainSingle().Subject;
        fromComponent.IsInverse.Should().BeFalse();
        fromComponent.OtherReference.Should().Be("P-0001");
        fromComponent.Quantity.Should().Be(2);

        var fromAssembly = (await links.ListAsync(assembly.Id)).Value.Should().ContainSingle().Subject;
        fromAssembly.IsInverse.Should().BeTrue();
        fromAssembly.OtherReference.Should().Be("P-0002");

        (await links.RemoveAsync(fromAssembly.Id)).IsSuccess.Should().BeTrue();
        (await links.ListAsync(component.Id)).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Invalid_links_are_refused()
    {
        await using var app = await TestApplication.CreateAsync();
        var data = new CatalogTestData(app);
        await data.SeedAsync();
        var a = await data.CreatePartAsync("P-0001", "Axe");
        var b = await data.CreatePartAsync("P-0002", "Bague");
        var links = app.Get<IPartLinkService>();

        (await links.AddAsync(new PartLinkInput { PartId = a.Id, OtherPartId = a.Id })).IsFailure.Should().BeTrue();
        (await links.AddAsync(new PartLinkInput { PartId = a.Id })).IsFailure.Should().BeTrue();
        (await links.AddAsync(new PartLinkInput { PartId = a.Id, OtherPartId = b.Id, Quantity = -1 })).IsFailure.Should().BeTrue();
        (await links.AddAsync(new PartLinkInput { PartId = a.Id, OtherPartId = b.Id, Type = PartLinkType.Related })).IsSuccess.Should().BeTrue();

        // « Associée à » est symétrique : le même lien saisi depuis l'autre fiche est un doublon.
        var duplicate = await links.AddAsync(new PartLinkInput { PartId = b.Id, OtherPartId = a.Id, Type = PartLinkType.Related });
        duplicate.Errors.Should().ContainSingle(e => e.Kind == ErrorKind.Conflict);
    }

    [Fact]
    public async Task A_reader_cannot_add_or_remove_links()
    {
        await using var app = await TestApplication.CreateAsync();
        var data = new CatalogTestData(app);
        await data.SeedAsync();
        var a = await data.CreatePartAsync("P-0001", "Axe");
        var b = await data.CreatePartAsync("P-0002", "Bague");
        var links = app.Get<IPartLinkService>();
        await links.AddAsync(new PartLinkInput { PartId = a.Id, OtherPartId = b.Id, Type = PartLinkType.Replaces });
        var link = (await links.ListAsync(a.Id)).Value.Single();
        app.User.As(UserRole.Reader);

        (await links.AddAsync(new PartLinkInput { PartId = a.Id, OtherPartId = b.Id, Type = PartLinkType.Variant })).Errors.Should().ContainSingle(e => e.Kind == ErrorKind.Forbidden);
        (await links.RemoveAsync(link.Id)).Errors.Should().ContainSingle(e => e.Kind == ErrorKind.Forbidden);
        (await links.ListAsync(b.Id)).Value.Should().ContainSingle(l => l.IsInverse && l.Type == PartLinkType.Replaces);
    }
}
