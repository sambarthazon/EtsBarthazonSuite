using System.Text.Json;
using Barthazon.Shared.Kernel.Results;

namespace Barthazon.Shared.UI.Components;

/// <summary>
/// État d'un formulaire : erreurs de validation renvoyées par le service (par champ)
/// et détection des modifications non enregistrées (instantané du modèle).
/// </summary>
public sealed class FormState
{
    private Dictionary<string, string> _fieldErrors = new(StringComparer.Ordinal);
    private string? _snapshot;

    /// <summary>Erreurs non rattachées à un champ.</summary>
    public IReadOnlyList<string> GeneralErrors { get; private set; } = [];

    /// <summary>Vrai si la dernière erreur est un conflit de concurrence.</summary>
    public bool HasConflict { get; private set; }

    public bool HasError(string field) => _fieldErrors.ContainsKey(field);

    public string? ErrorFor(string field) => _fieldErrors.GetValueOrDefault(field);

    /// <summary>Mémorise les erreurs d'un résultat en échec ; efface les erreurs en cas de succès.</summary>
    public void Apply(Result result)
    {
        ArgumentNullException.ThrowIfNull(result);
        _fieldErrors = result.Errors
            .Where(e => e.Field is not null)
            .GroupBy(e => e.Field!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().Message, StringComparer.Ordinal);
        GeneralErrors = result.Errors.Where(e => e.Field is null).Select(e => e.Message).ToList();
        HasConflict = result.HasError(ErrorKind.Conflict);
    }

    public void Clear()
    {
        _fieldErrors.Clear();
        GeneralErrors = [];
        HasConflict = false;
    }

    /// <summary>Prend un instantané du modèle (après chargement ou enregistrement).</summary>
    public void Snapshot(object model) => _snapshot = JsonSerializer.Serialize(model);

    /// <summary>Vrai si le modèle a changé depuis l'instantané.</summary>
    public bool IsDirty(object model) => _snapshot is not null && _snapshot != JsonSerializer.Serialize(model);
}
