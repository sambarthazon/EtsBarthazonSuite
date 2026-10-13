using Barthazon.Catalog.Contracts;
using MudBlazor;

namespace Barthazon.Catalog.UI.Components;

/// <summary>Icône d'un type de document.</summary>
public static class DocumentIcon
{
    public static string For(DocumentType type, DocumentKind kind) => kind == DocumentKind.Paper
        ? Icons.Material.Filled.Inventory2
        : type switch
        {
            DocumentType.Drawing => Icons.Material.Filled.Architecture,
            DocumentType.Model3D => Icons.Material.Filled.ViewInAr,
            DocumentType.NcProgram => Icons.Material.Filled.Code,
            DocumentType.InspectionSheet => Icons.Material.Filled.FactCheck,
            DocumentType.Photo => Icons.Material.Filled.PhotoCamera,
            DocumentType.Routing => Icons.Material.Filled.Route,
            DocumentType.Certificate => Icons.Material.Filled.WorkspacePremium,
            _ => Icons.Material.Filled.InsertDriveFile,
        };
}
