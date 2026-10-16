using Barthazon.Shared.Infrastructure.Hosting;
using Microsoft.Extensions.Configuration;

namespace Barthazon.Shared.Tests.Infrastructure;

public sealed class ConfigurationValidatorTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "bz-config-" + Guid.NewGuid().ToString("N"));

    private IConfiguration Build(Dictionary<string, string?> overrides)
    {
        var values = new Dictionary<string, string?>
        {
            ["App:PublicBaseUrl"] = "http://pc-bureau:5080",
            ["Database:Provider"] = "Sqlite",
            ["Database:ConnectionString"] = $"Data Source={Path.Combine(_root, "data", "b.db")}",
            ["FileStorage:RootPath"] = Path.Combine(_root, "files"),
            ["FileStorage:MaxFileSizeMb"] = "100",
            ["Backup:Enabled"] = "true",
            ["Backup:Directory"] = Path.Combine(_root, "backups"),
            ["Backup:DailyAt"] = "12:30",
            ["Backup:RetentionDays"] = "30",
            ["Catalog:ReferencePattern"] = "P-{0:0000}",
        };
        foreach (var (key, value) in overrides)
        {
            values[key] = value;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    [Fact]
    public void Valid_configuration_passes() =>
        ConfigurationValidator.Validate(Build([]), _root).IsValid.Should().BeTrue();

    [Theory]
    [InlineData("Database:Provider", "Oracle")]
    [InlineData("Backup:DailyAt", "midi")]
    [InlineData("App:PublicBaseUrl", "pc-bureau")]
    [InlineData("Catalog:ReferencePattern", "P-XXXX")]
    [InlineData("FileStorage:MaxFileSizeMb", "0")]
    public void Invalid_values_are_reported(string key, string value)
    {
        var report = ConfigurationValidator.Validate(Build(new() { [key] = value }), _root);

        report.IsValid.Should().BeFalse();
        report.Errors.Should().ContainSingle();
    }

    [Fact]
    public void Inaccessible_file_directory_is_an_error()
    {
        var blocker = Path.Combine(_root, "blocker");
        Directory.CreateDirectory(_root);
        File.WriteAllText(blocker, "fichier, pas dossier");

        var report = ConfigurationValidator.Validate(Build(new() { ["FileStorage:RootPath"] = Path.Combine(blocker, "files") }), _root);

        report.Errors.Should().ContainSingle(e => e.Contains("FileStorage:RootPath", StringComparison.Ordinal));
    }

    [Fact]
    public void Local_database_produces_no_warning()
    {
        var report = ConfigurationValidator.Validate(Build([]), _root);
        report.Warnings.Should().BeEmpty();
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
