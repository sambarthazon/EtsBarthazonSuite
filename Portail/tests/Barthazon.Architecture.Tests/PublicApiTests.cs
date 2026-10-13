using System.Reflection;
using PublicApiGenerator;

namespace Barthazon.Architecture.Tests;

/// <summary>
/// Tests d'approbation de l'API publique (section 6.5 de la spécification du stock) : l'API publique des contrats et du
/// socle est comparée à un fichier approuvé versionné (<c>ApprovedApi/*.approved.txt</c>). Toute modification fait échouer
/// le test tant que le fichier n'a pas été mis à jour consciemment : c'est le signal « ce changement peut impacter une
/// autre application ». Les contrats n'évoluent que par ajout.
/// <para>
/// Pour approuver un changement voulu : relire <c>ApprovedApi/&lt;Assemblage&gt;.received.txt</c> (écrit par le test),
/// vérifier qu'il ne fait qu'ajouter, puis le renommer en <c>.approved.txt</c>.
/// </para>
/// </summary>
public sealed class PublicApiTests
{
    public static TheoryData<string> Assemblies() =>
    [
        "Barthazon.Shared.Kernel",
        "Barthazon.Shared.UI",
        "Barthazon.Referential.Contracts",
        "Barthazon.Catalog.Contracts",
    ];

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void Public_api_matches_the_approved_file(string assemblyName)
    {
        var api = Assembly.Load(assemblyName).GeneratePublicApi(new ApiGeneratorOptions
        {
            IncludeAssemblyAttributes = false,
            ExcludeAttributes = ["System.Runtime.CompilerServices.NullableContextAttribute", "System.Runtime.CompilerServices.NullableAttribute"],
        }).Replace("\r\n", "\n", StringComparison.Ordinal);

        var directory = Path.Combine(SolutionProjects.Root, "Portail", "tests", "Barthazon.Architecture.Tests", "ApprovedApi");
        var approvedFile = Path.Combine(directory, assemblyName + ".approved.txt");
        var receivedFile = Path.Combine(directory, assemblyName + ".received.txt");
        var approved = File.Exists(approvedFile) ? File.ReadAllText(approvedFile).Replace("\r\n", "\n", StringComparison.Ordinal) : null;

        if (approved == api)
        {
            File.Delete(receivedFile);
            return;
        }

        File.WriteAllText(receivedFile, api);
        approved.Should().Be(api,
            $"l'API publique de {assemblyName} a changé : vérifier l'impact sur les autres applications, puis approuver en renommant {Path.GetFileName(receivedFile)} en {Path.GetFileName(approvedFile)}");
    }
}
