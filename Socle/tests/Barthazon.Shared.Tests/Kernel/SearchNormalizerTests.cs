using Barthazon.Shared.Kernel.Text;

namespace Barthazon.Shared.Tests.Kernel;

public class SearchNormalizerTests
{
    [Theory]
    [InlineData("Équerre ÉPAISSE", "equerre epaisse")]
    [InlineData("  Bride   à  souder ", "bride a souder")]
    [InlineData("Cœur", "coeur")]
    [InlineData(null, "")]
    public void Normalize_lowercases_and_removes_accents(string? input, string expected) =>
        SearchNormalizer.Normalize(input).Should().Be(expected);

    [Theory]
    [InlineData("AB-123.4", "ab1234")]
    [InlineData("ab 12/34_x", "ab1234x")]
    [InlineData("Ø60 × 125", "o60125")]
    public void Compact_removes_separators(string input, string expected) =>
        SearchNormalizer.Compact(input).Should().Be(expected);

    [Fact]
    public void Reference_with_separators_matches_compact_query()
    {
        var text = SearchNormalizer.BuildSearchText(["AB-123.4", "Arbre de transmission"]);

        SearchNormalizer.Matches(text, "ab1234").Should().BeTrue();
        SearchNormalizer.Matches(text, "AB-123.4").Should().BeTrue();
        SearchNormalizer.Matches(text, "ab 123").Should().BeTrue();
    }

    [Fact]
    public void All_words_must_be_found()
    {
        var text = SearchNormalizer.BuildSearchText(["Arbre de transmission", "DUPONT"]);

        SearchNormalizer.Matches(text, "arbre dupont").Should().BeTrue();
        SearchNormalizer.Matches(text, "arbre martin").Should().BeFalse();
    }

    [Fact]
    public void Accents_are_ignored_in_query_and_text()
    {
        var text = SearchNormalizer.BuildSearchText(["Pièce Équilibrée"]);

        SearchNormalizer.Matches(text, "piece equilibree").Should().BeTrue();
        SearchNormalizer.Matches(text, "PIÈCE").Should().BeTrue();
    }

    [Fact]
    public void Empty_query_matches_everything() =>
        SearchNormalizer.Matches("|x|", "   ").Should().BeTrue();
}

public class SearchHighlighterTests
{
    [Fact]
    public void Highlights_accent_insensitive_match()
    {
        var segments = SearchHighlighter.Split("Pièce équilibrée", "equi");

        segments.Should().ContainSingle(s => s.IsMatch).Which.Text.Should().Be("équi");
    }

    [Fact]
    public void Highlights_reference_across_separators()
    {
        var segments = SearchHighlighter.Split("Réf AB-123.4 client", "ab1234");

        segments.Where(s => s.IsMatch).Select(s => s.Text).Should().Equal("AB-123.4");
    }

    [Fact]
    public void Without_query_returns_whole_text()
    {
        SearchHighlighter.Split("Texte", null).Should().Equal(new HighlightSegment("Texte", false));
    }
}
