using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Services;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Testing;

namespace Barthazon.Catalog.Tests;

public sealed class ClassificationServiceTests : IAsyncLifetime
{
    private TestApplication _app = default!;

    private IClassificationService Service => _app.Get<IClassificationService>();

    public async Task InitializeAsync() => _app = await CatalogTestApp.CreateAsync();

    public async Task DisposeAsync() => await _app.DisposeAsync();

    [Fact]
    public async Task Families_are_unique_and_editable()
    {
        var family = (await Service.SaveFamilyAsync(null, new FamilyInput { Name = "Arbres" })).Value;

        (await Service.SaveFamilyAsync(null, new FamilyInput { Name = "arbres" })).Errors.Should().ContainSingle(e => e.Field == nameof(FamilyInput.Name));
        var renamed = await Service.SaveFamilyAsync(family.Id, new FamilyInput { Name = "Arbres de transmission", Version = family.Version });
        renamed.Value.Name.Should().Be("Arbres de transmission");
    }

    [Fact]
    public async Task Tags_keep_their_color_and_archived_are_hidden()
    {
        var tag = (await Service.SaveTagAsync(null, new TagInput { Label = "Sécurité", Color = TagColor.Error })).Value;
        await Service.SetTagArchivedAsync(tag.Id, true);

        (await Service.ListTagsAsync(false)).Value.Should().BeEmpty();
        (await Service.ListTagsAsync(true)).Value.Should().ContainSingle(t => t.Color == TagColor.Error && t.IsArchived);
    }

    [Fact]
    public async Task Unused_tag_can_be_deleted()
    {
        var tag = (await Service.SaveTagAsync(null, new TagInput { Label = "Provisoire" })).Value;

        (await Service.DeleteTagAsync(tag.Id)).IsSuccess.Should().BeTrue();
        (await Service.ListTagsAsync(true)).Value.Should().BeEmpty();
    }

    [Fact]
    public async Task Reader_cannot_modify()
    {
        _app.User.As(UserRole.Reader);

        (await Service.SaveFamilyAsync(null, new FamilyInput { Name = "X" })).HasError(ErrorKind.Forbidden).Should().BeTrue();
    }
}
