using Barthazon.Stock.Core;
using Microsoft.Extensions.Configuration;

namespace Barthazon.Stock.Tests;

/// <summary>Section « Stock » de la configuration, validée au démarrage (section 13).</summary>
public sealed class StockOptionsTests
{
    private static IConfiguration Configuration(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>("Stock:" + v.Key, v.Value))).Build();

    [Fact]
    public void Defaults_match_the_specification_and_are_valid()
    {
        var configuration = Configuration();
        var options = StockOptions.From(configuration);
        options.ItemReferencePattern.Should().Be("A-{0:0000}");
        options.BatchNumberPattern.Should().Be("L{0:yyyy}-{1:000}");
        options.DefaultStorageLocationCode.Should().Be("MAG");
        options.DefaultWorkInProgressLocationCode.Should().Be("ATELIER");
        options.AllowNegativeItemStock.Should().BeFalse();
        (options.BackdatingDays, options.SelfUndoMinutes, options.StaleBatchDays).Should().Be((31, 15, 14));
        StockOptions.Validate(configuration).Should().BeEmpty();
    }

    [Fact]
    public void Configured_values_are_read()
    {
        var options = StockOptions.From(Configuration(("BackdatingDays", "7"), ("AllowNegativeItemStock", "true"), ("ItemReferencePattern", "ART-{0:000}")));
        options.BackdatingDays.Should().Be(7);
        options.AllowNegativeItemStock.Should().BeTrue();
        options.ItemReferencePattern.Should().Be("ART-{0:000}");
    }

    [Theory]
    [InlineData("ItemReferencePattern", "A-0001")]
    [InlineData("ItemReferencePattern", "A-{0:0000")]
    [InlineData("BatchNumberPattern", "L{0:yyyy}")]
    [InlineData("BatchNumberPattern", "L{2}")]
    [InlineData("BackdatingDays", "-1")]
    [InlineData("BackdatingDays", "trente")]
    [InlineData("StaleBatchDays", "0")]
    [InlineData("AllowNegativeItemStock", "oui")]
    [InlineData("DefaultStorageLocationCode", " ")]
    public void Invalid_values_are_reported(string key, string value)
    {
        StockOptions.Validate(Configuration((key, value))).Should().NotBeEmpty();
    }
}
