using System.Xml.Linq;

namespace Barthazon.Architecture.Tests;

/// <summary>Projet de la solution, lu depuis son .csproj.</summary>
internal sealed record ProjectInfo(
    string Name,
    string Directory,
    IReadOnlyList<string> ProjectReferences,
    IReadOnlyList<string> PackageReferences,
    IReadOnlyList<string> FrameworkReferences)
{
    public bool IsCore => Name.EndsWith(".Core", StringComparison.Ordinal);

    public bool IsUi => Name.EndsWith(".UI", StringComparison.Ordinal);

    public bool IsInfrastructure => Name.EndsWith(".Infrastructure", StringComparison.Ordinal);

    public bool IsContracts => Name.EndsWith(".Contracts", StringComparison.Ordinal);

    private static readonly string[] ModuleLayers = ["Contracts", "Core", "Infrastructure", "UI"];

    /// <summary>
    /// Nom du module (ex. « Catalog ») d'après le nom du projet (<c>Barthazon.&lt;Module&gt;.&lt;Couche&gt;</c>), sinon null.
    /// Le socle commun (<c>Barthazon.Shared.*</c>) n'est pas un module.
    /// </summary>
    public string? Module
    {
        get
        {
            var parts = Name.Split('.');
            return parts.Length == 3 && parts[1] != "Shared" && ModuleLayers.Contains(parts[2]) ? parts[1] : null;
        }
    }

    /// <summary>Dossier d'application à la racine du dépôt (Socle, Annuaire, Stock, Portail).</summary>
    public string Area => System.IO.Path.GetRelativePath(SolutionProjects.Root, Directory).Replace('\\', '/').Split('/')[0];
}

/// <summary>Lecture des projets de la solution.</summary>
internal static class SolutionProjects
{
    private static readonly Lazy<string> RootLazy = new(FindRoot);
    private static readonly Lazy<IReadOnlyDictionary<string, ProjectInfo>> ProjectsLazy = new(Load);

    public static string Root => RootLazy.Value;

    public static IReadOnlyDictionary<string, ProjectInfo> All => ProjectsLazy.Value;

    public static IEnumerable<ProjectInfo> Source => All.Values.Where(p => p.Directory.Replace('\\', '/').Contains("/src/", StringComparison.Ordinal));

    /// <summary>Dossiers de sources de toutes les applications (Socle/src, Annuaire/src…).</summary>
    public static IEnumerable<string> SourceDirectories =>
        System.IO.Directory.EnumerateDirectories(Root).Select(d => Path.Combine(d, "src")).Where(System.IO.Directory.Exists);

    /// <summary>Fermeture transitive des références de projet.</summary>
    public static IReadOnlySet<string> TransitiveReferences(ProjectInfo project)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        var stack = new Stack<string>(project.ProjectReferences);
        while (stack.Count > 0)
        {
            var name = stack.Pop();
            if (!result.Add(name))
            {
                continue;
            }

            if (All.TryGetValue(name, out var referenced))
            {
                foreach (var r in referenced.ProjectReferences)
                {
                    stack.Push(r);
                }
            }
        }

        return result;
    }

    private static string FindRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Barthazon.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Barthazon.sln introuvable.");
    }

    private static Dictionary<string, ProjectInfo> Load()
    {
        var projects = new Dictionary<string, ProjectInfo>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(Root, "*.csproj", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                || file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            {
                continue;
            }

            var xml = XDocument.Load(file);
            static IReadOnlyList<string> Includes(XDocument doc, string element) =>
                doc.Descendants(element).Select(e => (string?)e.Attribute("Include")).OfType<string>().ToList();

            var name = Path.GetFileNameWithoutExtension(file);
            projects[name] = new ProjectInfo(
                name,
                Path.GetDirectoryName(file)!,
                Includes(xml, "ProjectReference").Select(r => Path.GetFileNameWithoutExtension(r.Replace('\\', '/'))).ToList(),
                Includes(xml, "PackageReference"),
                Includes(xml, "FrameworkReference"));
        }

        return projects;
    }
}
