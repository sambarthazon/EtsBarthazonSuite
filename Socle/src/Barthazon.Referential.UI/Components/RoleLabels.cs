using Barthazon.Referential.UI.Resources;
using Barthazon.Shared.Kernel.Security;

namespace Barthazon.Referential.UI.Components;

public static class RoleLabels
{
    public static string Label(this UserRole role) => role switch
    {
        UserRole.Administrator => ReferentialTexts.RoleAdministrator,
        UserRole.Editor => ReferentialTexts.RoleEditor,
        _ => ReferentialTexts.RoleReader,
    };
}
