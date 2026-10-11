using System.Security.Cryptography;
using Barthazon.Shared.Infrastructure.Files;
using Barthazon.Shared.Kernel.Files;

namespace Barthazon.Shared.Tests.Infrastructure;

public sealed class FileSystemFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bz-files-" + Guid.NewGuid().ToString("N"));
    private readonly FileSystemFileStorage _storage;

    public FileSystemFileStorageTests()
    {
        _storage = new FileSystemFileStorage(new FileStorageOptions { RootPath = _root });
    }

    [Fact]
    public async Task Save_computes_size_and_sha256_and_can_be_read_back()
    {
        var bytes = RandomNumberGenerator.GetBytes(300_000);
        using var input = new MemoryStream(bytes);

        var stored = await _storage.SaveAsync("parts/a/b/plan.pdf", input, 1_000_000);

        stored.SizeBytes.Should().Be(bytes.Length);
        stored.Sha256.Should().Be(Convert.ToHexStringLower(SHA256.HashData(bytes)));
        (await _storage.ExistsAsync("parts/a/b/plan.pdf")).Should().BeTrue();
        await using var read = await _storage.OpenReadAsync("parts/a/b/plan.pdf");
        using var copy = new MemoryStream();
        await read.CopyToAsync(copy);
        copy.ToArray().Should().Equal(bytes);
    }

    [Fact]
    public async Task File_names_with_double_dots_are_accepted_but_parent_segments_are_not()
    {
        using var input = new MemoryStream([1, 2, 3]);

        (await _storage.SaveAsync("parts/a/b/plan..v2.pdf", input, 1024)).SizeBytes.Should().Be(3);
        (await _storage.ExistsAsync("parts/a/b/plan..v2.pdf")).Should().BeTrue();
        var escape = () => _storage.ExistsAsync("parts/../../secret.txt");
        await escape.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task Too_large_file_is_rejected_and_nothing_is_left_on_disk()
    {
        using var input = new MemoryStream(new byte[2048]);

        var act = () => _storage.SaveAsync("parts/x/big.bin", input, 1024);

        await act.Should().ThrowAsync<FileTooLargeException>();
        Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Theory]
    [InlineData("../evil.txt")]
    [InlineData("parts/../../evil.txt")]
    [InlineData("/etc/passwd")]
    public void Keys_outside_root_are_refused(string key)
    {
        var act = () => _storage.Resolve(key);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task Delete_removes_file()
    {
        using var input = new MemoryStream([1, 2, 3]);
        await _storage.SaveAsync("parts/p/r/d.txt", input, 100);

        await _storage.DeleteAsync("parts/p/r/d.txt");

        (await _storage.ExistsAsync("parts/p/r/d.txt")).Should().BeFalse();
    }

    [Theory]
    [InlineData("Plan indice B (définitif).pdf", "Plan_indice_B__definitif_.pdf")]
    [InlineData("../../x.step", "x.step")]
    [InlineData("éé", "ee")]
    public void File_names_are_sanitized(string input, string expected) =>
        FileNames.Sanitize(input).Should().Be(expected);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
