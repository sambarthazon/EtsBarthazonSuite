using System.Text.RegularExpressions;

namespace Barthazon.Architecture.Tests;

/// <summary>Pièges MudBlazor 9 repérés en recette, vérifiés sur tous les fichiers .razor.</summary>
public partial class UiRulesTests
{
    [Fact]
    public void Custom_menu_activators_open_the_menu_themselves()
    {
        // Avec MudBlazor 9, un ActivatorContent n'ouvre pas le menu tout seul : il doit appeler context.ToggleAsync / OpenAsync.
        var offenders = RazorFiles()
            .SelectMany(file => ActivatorPattern().Matches(File.ReadAllText(file))
                .Where(m => !m.Groups["body"].Value.Contains("context.", StringComparison.Ordinal))
                .Select(_ => Path.GetRelativePath(SolutionProjects.Root, file)))
            .ToList();

        offenders.Should().BeEmpty("un menu dont le déclencheur n'appelle pas context.ToggleAsync ne s'ouvre jamais");
    }

    private static IEnumerable<string> RazorFiles() =>
        Directory.EnumerateFiles(Path.Combine(SolutionProjects.Root, "src"), "*.razor", SearchOption.AllDirectories)
            .Where(f => !f.Replace('\\', '/').Contains("/obj/", StringComparison.Ordinal));

    [GeneratedRegex(@"<ActivatorContent>(?<body>.*?)</ActivatorContent>", RegexOptions.Singleline)]
    private static partial Regex ActivatorPattern();
}
