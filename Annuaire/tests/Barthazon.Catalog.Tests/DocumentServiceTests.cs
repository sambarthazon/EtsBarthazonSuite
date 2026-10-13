using System.Security.Cryptography;
using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Services;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Testing;

namespace Barthazon.Catalog.Tests;

public sealed class DocumentServiceTests : IAsyncLifetime
{
    private TestApplication _app = default!;
    private CatalogTestData _data = default!;
    private PartDetails _part = default!;

    private IDocumentService Documents => _app.Get<IDocumentService>();

    private string FilesRoot => Path.Combine(_app.Root, "files");

    public async Task InitializeAsync()
    {
        _app = await CatalogTestApp.CreateAsync();
        _data = new CatalogTestData(_app);
        await _data.SeedAsync();
        _part = await _data.CreatePartAsync("P-0200", "Bride");
    }

    public async Task DisposeAsync() => await _app.DisposeAsync();

    private Guid RevisionId => _part.CurrentRevisionId!.Value;

    [Fact]
    public async Task Uploaded_file_is_stored_on_disk_with_hash_and_can_be_opened()
    {
        var bytes = RandomNumberGenerator.GetBytes(10_000);
        using var stream = new MemoryStream(bytes);

        var document = (await Documents.AddFileAsync(_part.Id, RevisionId, new FileDocumentInput { Type = DocumentType.Drawing }, "Plan indice A.pdf", stream)).Value;

        document.Title.Should().Be("Plan indice A");
        document.ContentType.Should().Be("application/pdf");
        document.SizeBytes.Should().Be(bytes.Length);
        Directory.GetFiles(FilesRoot, "*", SearchOption.AllDirectories).Should().ContainSingle()
            .Which.Replace('\\', '/').Should().Contain($"parts/{_part.Id:N}/{RevisionId:N}/");
        var opened = (await Documents.OpenAsync(document.Id)).Value;
        await using (opened.Content)
        {
            using var copy = new MemoryStream();
            await opened.Content.CopyToAsync(copy);
            copy.ToArray().Should().Equal(bytes);
        }
    }

    [Fact]
    public async Task Unknown_extensions_are_accepted_but_served_as_binary()
    {
        using var stream = new MemoryStream([1, 2, 3]);

        var document = (await Documents.AddFileAsync(_part.Id, RevisionId, new FileDocumentInput { Type = DocumentType.Model3D }, "arbre.step", stream)).Value;

        document.ContentType.Should().Be("application/octet-stream");
        document.CanPreview.Should().BeFalse();
    }

    [Fact]
    public async Task Too_large_file_is_refused_without_leaving_a_document()
    {
        using var stream = new MemoryStream(new byte[6 * 1024 * 1024]);

        var result = await Documents.AddFileAsync(_part.Id, RevisionId, new FileDocumentInput(), "gros.pdf", stream);

        result.Errors.Should().ContainSingle(e => e.Field == "File");
        (await _data.Parts.GetRevisionAsync(_part.Id, RevisionId)).Value.Documents.Should().BeEmpty();
        Directory.Exists(FilesRoot).Should().BeTrue();
        Directory.GetFiles(FilesRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task Paper_document_requires_location_and_can_receive_a_scan_later()
    {
        var missing = await Documents.AddPaperAsync(_part.Id, RevisionId, new PaperDocumentInput { Title = "Plan" });
        missing.Errors.Should().ContainSingle(e => e.Field == nameof(PaperDocumentInput.PaperLocation));

        var paper = (await Documents.AddPaperAsync(_part.Id, RevisionId, new PaperDocumentInput { Title = "Plan client", PaperLocation = "Armoire 2" })).Value;
        paper.Kind.Should().Be(DocumentKind.Paper);

        using var scan = new MemoryStream([0x25, 0x50, 0x44, 0x46]);
        var scanned = (await Documents.AttachFileToPaperAsync(_part.Id, paper.Id, "scan.pdf", scan)).Value;

        scanned.Kind.Should().Be(DocumentKind.File);
        scanned.PaperLocation.Should().Be("Armoire 2", "l'emplacement de l'original reste connu");
        scanned.IsPdf.Should().BeTrue();
    }

    [Fact]
    public async Task Removing_a_document_deletes_the_file_only_when_no_other_revision_uses_it()
    {
        using var stream = new MemoryStream([1, 2, 3]);
        var document = (await Documents.AddFileAsync(_part.Id, RevisionId, new FileDocumentInput(), "plan.pdf", stream)).Value;
        var b = (await _data.Revisions.CreateAsync(_part.Id, new NewRevisionInput { SourceRevisionId = RevisionId, Index = "B", DocumentIdsToCopy = [document.Id] })).Value;

        await Documents.RemoveAsync(_part.Id, document.Id);
        Directory.GetFiles(FilesRoot, "*", SearchOption.AllDirectories).Should().ContainSingle("l'indice B utilise encore le fichier");

        await Documents.RemoveAsync(_part.Id, b.Documents.Single().Id);
        Directory.GetFiles(FilesRoot, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task First_photo_becomes_the_thumbnail()
    {
        using var stream = new MemoryStream([137, 80, 78, 71]);

        var photo = (await Documents.AddFileAsync(_part.Id, RevisionId, new FileDocumentInput { Type = DocumentType.Photo }, "photo.jpg", stream)).Value;

        (await _data.Parts.GetAsync(_part.Id)).Value.MainPhotoDocumentId.Should().Be(photo.Id);
        (await _data.Parts.SearchAsync(new PartSearchRequest())).Value.Items.Single().ThumbnailDocumentId.Should().Be(photo.Id);
    }

    [Fact]
    public async Task Readers_can_open_but_not_add_documents()
    {
        using var stream = new MemoryStream([1]);
        var document = (await Documents.AddFileAsync(_part.Id, RevisionId, new FileDocumentInput(), "a.pdf", stream)).Value;
        _app.User.As(UserRole.Reader);

        using var other = new MemoryStream([1]);
        (await Documents.AddFileAsync(_part.Id, RevisionId, new FileDocumentInput(), "b.pdf", other)).HasError(ErrorKind.Forbidden).Should().BeTrue();
        var opened = await Documents.OpenAsync(document.Id);
        opened.IsSuccess.Should().BeTrue();
        await opened.Value.Content.DisposeAsync();
    }

    [Fact]
    public async Task Documents_are_traced_in_history()
    {
        await Documents.AddPaperAsync(_part.Id, RevisionId, new PaperDocumentInput { Title = "Plan papier", PaperLocation = "Bureau" });

        (await _data.Parts.GetHistoryAsync(_part.Id)).Value.Should().Contain(h => h.Action == "DocumentAdded" && h.Note!.Contains("Plan papier"));
    }

    [Fact]
    public async Task Drawing_filter_and_paper_only_quality_check()
    {
        await Documents.AddPaperAsync(_part.Id, RevisionId, new PaperDocumentInput { Title = "Plan", Type = DocumentType.Drawing, PaperLocation = "Bureau" });
        await _data.CreatePartAsync("P-0201", "Sans plan");

        (await _data.Parts.SearchAsync(new PartSearchRequest { HasDrawing = true })).Value.Items.Select(i => i.InternalReference).Should().Equal("P-0200");
        (await _data.Parts.SearchAsync(new PartSearchRequest { HasDrawing = false })).Value.Items.Select(i => i.InternalReference).Should().Equal("P-0201");
        (await _data.Parts.SearchAsync(new PartSearchRequest { Quality = Core.Persistence.PartQualityIssue.PaperDrawingOnly })).Value.Items.Select(i => i.InternalReference).Should().Equal("P-0200");
    }
}
