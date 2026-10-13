namespace Barthazon.Shared.Kernel.Modules;

/// <summary>Liste des modules déclarés auprès du portail.</summary>
public interface IModuleCatalog
{
    IReadOnlyList<IModule> Modules { get; }

    /// <summary>Module auquel appartient l'adresse (chemin relatif), ou null (portail, administration).</summary>
    IModule? FindByPath(string relativePath);
}

/// <summary>Implémentation simple, construite au démarrage.</summary>
public sealed class ModuleCatalog(IEnumerable<IModule> modules) : IModuleCatalog
{
    public IReadOnlyList<IModule> Modules { get; } = modules.OrderBy(m => m.Order).ToList();

    public IModule? FindByPath(string relativePath)
    {
        var path = "/" + (relativePath ?? string.Empty).TrimStart('/');
        return Modules.FirstOrDefault(m => m.RoutePrefixes.Any(prefix =>
            path.Equals(prefix, StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(prefix.TrimEnd('/') + "/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith(prefix.TrimEnd('/') + "?", StringComparison.OrdinalIgnoreCase)));
    }
}
