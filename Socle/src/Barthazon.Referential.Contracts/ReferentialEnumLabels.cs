using System.Globalization;
using Barthazon.Referential.Contracts.Resources;

namespace Barthazon.Referential.Contracts;

/// <summary>Libellés français des énumérations du référentiel.</summary>
public static class ReferentialEnumLabels
{
    public static string Label(this MaterialFamily family) =>
        ReferentialLabels.ResourceManager.GetString("MaterialFamily_" + family, CultureInfo.CurrentUICulture) ?? family.ToString();

    public static string Label(this MachineType type) =>
        ReferentialLabels.ResourceManager.GetString("MachineType_" + type, CultureInfo.CurrentUICulture) ?? type.ToString();
}
