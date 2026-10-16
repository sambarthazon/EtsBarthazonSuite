using Barthazon.Catalog.Contracts;
using Barthazon.Shared.Kernel.Domain;

namespace Barthazon.Stock.Core.Domain;

/// <summary>Ce que consomme le lancement d'une pièce : un article, ou une autre pièce de l'annuaire.</summary>
public enum BlankSourceKind
{
    Item,
    Part,
}

/// <summary>
/// Pièce suivie : paramètres de stock d'une pièce de l'annuaire (section 7.3). Créée explicitement ou au premier
/// mouvement. Garde un instantané de la référence et de la désignation, affiché si la pièce devient introuvable.
/// </summary>
public sealed class TrackedPart : AuditableEntity
{
    public const int ReferenceMaxLength = 50;
    public const int DesignationMaxLength = 200;
    public const int NotesMaxLength = 4000;

    private readonly List<BlankSource> _sources = [];

    private TrackedPart()
    {
    }

    public TrackedPart(PartSummary part)
    {
        ArgumentNullException.ThrowIfNull(part);
        PartId = part.Id;
        Refresh(part);
    }

    public Guid PartId { get; private set; }

    public string LastKnownReference { get; private set; } = string.Empty;

    public string LastKnownDesignation { get; private set; } = string.Empty;

    /// <summary>Seuil de pièces finies (tous indices).</summary>
    public int? MinimumThreshold { get; set; }

    /// <summary>Rangement des pièces finies.</summary>
    public Guid? DefaultLocationId { get; set; }

    public string? Notes { get; set; }

    public IReadOnlyList<BlankSource> Sources => _sources;

    /// <summary>Met à jour l'instantané (seulement s'il a changé : une lecture n'écrit jamais en base).</summary>
    public bool Refresh(PartSummary part)
    {
        ArgumentNullException.ThrowIfNull(part);
        var reference = Truncate(part.InternalReference, ReferenceMaxLength);
        var designation = Truncate(part.Designation, DesignationMaxLength);
        if (reference == LastKnownReference && designation == LastKnownDesignation)
        {
            return false;
        }

        LastKnownReference = reference;
        LastKnownDesignation = designation;
        return true;
    }

    public BlankSource AddSource(BlankSourceKind kind, Guid? itemId, Guid? sourcePartId, int? afterOperation, decimal quantityPerPart, Guid? revisionId)
    {
        var source = new BlankSource(Id, kind, itemId, sourcePartId, afterOperation, quantityPerPart, revisionId)
        {
            SortOrder = _sources.Count == 0 ? 10 : _sources.Max(s => s.SortOrder) + 10,
        };
        _sources.Add(source);
        return source;
    }

    public void RemoveSource(BlankSource source) => _sources.Remove(source);

    /// <summary>Une seule source par défaut par portée (tous indices, ou un indice donné).</summary>
    public void MakeDefault(BlankSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        foreach (var other in _sources.Where(s => s.RevisionId == source.RevisionId))
        {
            other.IsDefault = other == source;
        }
    }

    /// <summary>Sources utilisables pour un indice : celles de l'indice d'abord, puis celles valables pour tous les indices.</summary>
    public IReadOnlyList<BlankSource> SourcesFor(Guid revisionId) =>
        _sources.Where(s => s.RevisionId == revisionId)
            .OrderByDescending(s => s.IsDefault).ThenBy(s => s.SortOrder)
            .Concat(_sources.Where(s => s.RevisionId is null).OrderByDescending(s => s.IsDefault).ThenBy(s => s.SortOrder))
            .ToList();

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>
/// Source de brut (section 7.4) : ce qu'on consomme pour lancer la pièce. Plusieurs pièces peuvent déclarer le même
/// article (brut commun) ou la même pièce à une étape de sa gamme (semi-fini commun).
/// </summary>
public sealed class BlankSource : Entity, IAuditChild
{
    public const int NoteMaxLength = 500;

    private BlankSource()
    {
    }

    internal BlankSource(Guid trackedPartId, BlankSourceKind kind, Guid? itemId, Guid? sourcePartId, int? afterOperation, decimal quantityPerPart, Guid? revisionId)
    {
        TrackedPartId = trackedPartId;
        Kind = kind;
        ItemId = kind == BlankSourceKind.Item ? itemId : null;
        SourcePartId = kind == BlankSourceKind.Part ? sourcePartId : null;
        SourceAfterOperation = kind == BlankSourceKind.Part ? afterOperation : null;
        QuantityPerPart = quantityPerPart;
        RevisionId = revisionId;
    }

    public Guid TrackedPartId { get; private set; }

    /// <summary>Vide = valable pour tous les indices ; sinon seulement pour cet indice.</summary>
    public Guid? RevisionId { get; set; }

    public BlankSourceKind Kind { get; private set; }

    public Guid? ItemId { get; private set; }

    public Guid? SourcePartId { get; private set; }

    /// <summary>Pièce source : vide = finie ; sinon prise « après l'opération n° » de sa gamme (semi-fini commun).</summary>
    public int? SourceAfterOperation { get; private set; }

    /// <summary>Quantité de source par pièce produite, dans l'unité de la source.</summary>
    public decimal QuantityPerPart { get; set; }

    public bool IsDefault { get; set; }

    public int SortOrder { get; set; }

    public string? Note { get; set; }

    public string AuditParentType => nameof(TrackedPart);

    public Guid AuditParentId => TrackedPartId;

    public void Retarget(BlankSourceKind kind, Guid? itemId, Guid? sourcePartId, int? afterOperation)
    {
        Kind = kind;
        ItemId = kind == BlankSourceKind.Item ? itemId : null;
        SourcePartId = kind == BlankSourceKind.Part ? sourcePartId : null;
        SourceAfterOperation = kind == BlankSourceKind.Part ? afterOperation : null;
    }
}

/// <summary>Contrôle des cycles entre sources (A ne peut pas avoir B pour source si B dépend déjà de A).</summary>
public static class BlankSourceGraph
{
    /// <summary>
    /// Vrai si ajouter « <paramref name="partId"/> a pour source <paramref name="sourcePartId"/> » créerait un cycle.
    /// <paramref name="edges"/> : pour chaque pièce suivie, les pièces qu'elle déclare comme sources.
    /// </summary>
    public static bool WouldCreateCycle(Guid partId, Guid sourcePartId, IReadOnlyDictionary<Guid, IReadOnlyCollection<Guid>> edges)
    {
        ArgumentNullException.ThrowIfNull(edges);
        if (partId == sourcePartId)
        {
            return true;
        }

        // La source dépend-elle (directement ou non) de la pièce ?
        var visited = new HashSet<Guid>();
        var pending = new Stack<Guid>([sourcePartId]);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            if (!visited.Add(current) || !edges.TryGetValue(current, out var next))
            {
                continue;
            }

            foreach (var dependency in next)
            {
                if (dependency == partId)
                {
                    return true;
                }

                pending.Push(dependency);
            }
        }

        return false;
    }
}
