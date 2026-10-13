using System.Reflection;

namespace Barthazon.Architecture.Tests;

/// <summary>Règles de dépendance de la section 6.3 de la spécification.</summary>
public class DependencyRulesTests
{
    private static readonly string[] ForbiddenInCore = ["Microsoft.EntityFrameworkCore", "MudBlazor", "Microsoft.AspNetCore"];

    public static TheoryData<string> CoreProjects() => Names(p => p.IsCore);

    public static TheoryData<string> UiProjects() => Names(p => p.IsUi);

    public static TheoryData<string> ModuleProjects() => Names(p => p.Module is not null);

    [Fact]
    public void Solution_contains_expected_projects()
    {
        SolutionProjects.All.Keys.Should().Contain(
        [
            "Barthazon.Shared.Kernel", "Barthazon.Shared.Infrastructure", "Barthazon.Shared.UI",
            "Barthazon.Referential.Contracts", "Barthazon.Referential.Core", "Barthazon.Referential.Infrastructure", "Barthazon.Referential.UI",
            "Barthazon.Catalog.Contracts", "Barthazon.Catalog.Core", "Barthazon.Catalog.Infrastructure", "Barthazon.Catalog.UI",
            "Barthazon.Socle.Migrations.Sqlite", "Barthazon.Socle.Migrations.Postgres",
            "Barthazon.Catalog.Migrations.Sqlite", "Barthazon.Catalog.Migrations.Postgres",
            "Barthazon.Portal",
        ]);
    }

    [Theory]
    [MemberData(nameof(CoreProjects))]
    public void Core_references_only_kernel_and_contracts(string name)
    {
        var project = SolutionProjects.All[name];
        project.ProjectReferences.Should().NotContain(
            r => r != "Barthazon.Shared.Kernel" && !r.EndsWith(".Contracts", StringComparison.Ordinal),
            "un projet *.Core ne référence que Shared.Kernel et des *.Contracts");
        project.FrameworkReferences.Should().BeEmpty("un projet *.Core ne dépend pas d'ASP.NET Core");
        project.PackageReferences.Should().NotContain(p => ForbiddenInCore.Any(f => p.StartsWith(f, StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(CoreProjects))]
    public void Core_assembly_does_not_load_ef_aspnet_or_mudblazor(string name)
    {
        var assembly = Assembly.Load(name);
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Should()
            .NotContain(a => ForbiddenInCore.Any(f => a.StartsWith(f, StringComparison.Ordinal)));
    }

    [Theory]
    [MemberData(nameof(UiProjects))]
    public void Ui_never_references_infrastructure_or_ef_core(string name)
    {
        var project = SolutionProjects.All[name];
        SolutionProjects.TransitiveReferences(project).Should()
            .NotContain(r => r.EndsWith(".Infrastructure", StringComparison.Ordinal), "l'interface n'appelle que les services applicatifs");
        project.PackageReferences.Should().NotContain(p => p.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal));

        var assembly = Assembly.Load(name);
        assembly.GetReferencedAssemblies().Select(a => a.Name!).Should()
            .NotContain(a => a.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal) || a.EndsWith(".Infrastructure", StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(ModuleProjects))]
    public void Modules_reference_other_modules_only_through_contracts(string name)
    {
        var project = SolutionProjects.All[name];
        foreach (var reference in project.ProjectReferences)
        {
            if (!SolutionProjects.All.TryGetValue(reference, out var target) || target.Module is null || target.Module == project.Module)
            {
                continue;
            }

            target.IsContracts.Should().BeTrue($"{name} ne doit référencer le module {target.Module} que par son projet Contracts (trouvé : {reference})");
        }
    }

    [Fact]
    public void Shared_projects_never_reference_modules()
    {
        foreach (var project in SolutionProjects.Source.Where(p => p.Name.StartsWith("Barthazon.Shared.", StringComparison.Ordinal)))
        {
            SolutionProjects.TransitiveReferences(project).Should()
                .NotContain(r => !r.StartsWith("Barthazon.Shared.", StringComparison.Ordinal), $"{project.Name} est un socle commun");
        }
    }

    /// <summary>Dossiers qu'un projet de chaque dossier d'application a le droit de référencer (section 6.3 du stock).</summary>
    private static readonly Dictionary<string, string[]> AllowedAreas = new(StringComparer.Ordinal)
    {
        ["Socle"] = ["Socle"],
        ["Annuaire"] = ["Socle", "Annuaire"],
        ["Stock"] = ["Socle", "Stock"],
    };

    [Fact]
    public void Applications_reference_only_allowed_areas()
    {
        foreach (var project in SolutionProjects.Source.Where(p => AllowedAreas.ContainsKey(p.Area)))
        {
            foreach (var reference in SolutionProjects.TransitiveReferences(project))
            {
                var target = SolutionProjects.All[reference];

                // Exception : le stock lit l'annuaire par ses contrats.
                var allowed = AllowedAreas[project.Area].Contains(target.Area)
                    || (project.Area == "Stock" && reference == "Barthazon.Catalog.Contracts");
                allowed.Should().BeTrue($"{project.Name} ({project.Area}) ne doit pas référencer {reference} ({target.Area})");
            }
        }
    }

    [Fact]
    public void Contracts_reference_only_kernel_and_other_contracts()
    {
        foreach (var project in SolutionProjects.Source.Where(p => p.IsContracts))
        {
            project.ProjectReferences.Should().OnlyContain(
                r => r == "Barthazon.Shared.Kernel" || r.EndsWith(".Contracts", StringComparison.Ordinal),
                $"{project.Name} est un contrat");
        }
    }

    [Fact]
    public void Kernel_has_no_project_reference_and_only_abstractions()
    {
        var kernel = SolutionProjects.All["Barthazon.Shared.Kernel"];
        kernel.ProjectReferences.Should().BeEmpty();
        kernel.FrameworkReferences.Should().BeEmpty();
        kernel.PackageReferences.Should().NotContain(p => !p.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal) || !p.EndsWith(".Abstractions", StringComparison.Ordinal));
    }

    [Fact]
    public void Only_portal_references_both_ui_and_infrastructure()
    {
        foreach (var project in SolutionProjects.Source.Where(p => p.Name != "Barthazon.Portal"))
        {
            var references = SolutionProjects.TransitiveReferences(project);
            var hasUi = references.Any(r => r.EndsWith(".UI", StringComparison.Ordinal));
            var hasInfrastructure = references.Any(r => r.EndsWith(".Infrastructure", StringComparison.Ordinal));
            (hasUi && hasInfrastructure).Should().BeFalse($"seul le portail assemble interface et infrastructure ({project.Name})");
        }
    }

    [Fact]
    public void Central_package_management_no_version_in_projects()
    {
        foreach (var file in Directory.EnumerateFiles(SolutionProjects.Root, "*.csproj", SearchOption.AllDirectories))
        {
            File.ReadAllText(file).Should().NotMatchRegex("PackageReference[^>]*Version=", $"les versions sont centralisées dans Directory.Packages.props ({file})");
        }
    }

    private static TheoryData<string> Names(Func<ProjectInfo, bool> predicate)
    {
        var data = new TheoryData<string>();
        foreach (var project in SolutionProjects.Source.Where(predicate).OrderBy(p => p.Name))
        {
            data.Add(project.Name);
        }

        return data;
    }
}
