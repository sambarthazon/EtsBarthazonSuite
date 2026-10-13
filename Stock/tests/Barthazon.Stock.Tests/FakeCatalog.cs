using Barthazon.Catalog.Contracts;

namespace Barthazon.Stock.Tests;

/// <summary>
/// Doublure en mémoire des contrats de l'annuaire (<see cref="IPartLookup"/>, <see cref="IPartRoutingLookup"/>),
/// alimentée par chaque test : les tests du stock ne dépendent jamais de l'annuaire réel (section 6.5).
/// </summary>
public sealed class FakeCatalog : IPartLookup, IPartRoutingLookup
{
    private readonly Dictionary<Guid, PartSummary> _parts = [];
    private readonly Dictionary<Guid, PartRevisionSummary> _revisions = [];
    private readonly Dictionary<Guid, List<RoutingOperationSummary>> _routings = [];

    public Guid DefaultCustomerId { get; set; } = Guid.NewGuid();

    /// <summary>Ajoute une pièce avec un indice en vigueur et sa gamme (numéros 10, 20, 30… par défaut).</summary>
    public FakePart AddPart(string reference, string designation = "Pièce de test", string index = "A", params OperationType[] operations)
    {
        var part = new PartSummary(Guid.NewGuid(), reference, designation, DefaultCustomerId, null, index, RevisionStatus.InForce, PartStatus.Active, false);
        _parts[part.Id] = part;
        var revision = AddRevision(part.Id, index, RevisionStatus.InForce);
        var number = 10;
        foreach (var type in operations)
        {
            AddOperation(revision.Id, number, type);
            number += 10;
        }

        return new FakePart(part.Id, revision.Id);
    }

    public PartRevisionSummary AddRevision(Guid partId, string index, RevisionStatus status = RevisionStatus.Draft, Guid? materialId = null)
    {
        var revision = new PartRevisionSummary(Guid.NewGuid(), partId, index, status, null, materialId, null, null);
        _revisions[revision.Id] = revision;
        _routings[revision.Id] = [];
        if (status == RevisionStatus.InForce)
        {
            PutInForce(revision.Id);
        }

        return revision;
    }

    public RoutingOperationSummary AddOperation(Guid revisionId, int number, OperationType type, Guid? machineId = null, string? description = null,
        decimal? setupTimeMin = null, decimal? cycleTimeMin = null, string? subcontractor = null)
    {
        var operation = new RoutingOperationSummary(Guid.NewGuid(), number, type, machineId, description ?? type.ToString(), setupTimeMin, cycleTimeMin, subcontractor);
        _routings[revisionId].Add(operation);
        return operation;
    }

    public void RemoveOperation(Guid revisionId, int number) => _routings[revisionId].RemoveAll(o => o.Number == number);

    /// <summary>Met un indice en vigueur : l'ancien indice en vigueur passe à « remplacé ».</summary>
    public void PutInForce(Guid revisionId)
    {
        var revision = _revisions[revisionId];
        foreach (var other in _revisions.Values.Where(r => r.PartId == revision.PartId && r.Status == RevisionStatus.InForce && r.Id != revisionId).ToList())
        {
            _revisions[other.Id] = other with { Status = RevisionStatus.Superseded };
        }

        _revisions[revisionId] = revision with { Status = RevisionStatus.InForce };
        var part = _parts[revision.PartId];
        _parts[part.Id] = part with { CurrentRevisionIndex = revision.Index, CurrentRevisionStatus = RevisionStatus.InForce };
    }

    public void Archive(Guid partId) => _parts[partId] = _parts[partId] with { IsArchived = true };

    /// <summary>Simule une pièce supprimée de l'annuaire (base restaurée partiellement…).</summary>
    public void Forget(Guid partId) => _parts.Remove(partId);

    public Task<PartSummary?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_parts.GetValueOrDefault(id));

    public Task<PartSummary?> GetByReferenceAsync(string internalReference, CancellationToken cancellationToken = default) =>
        Task.FromResult(_parts.Values.FirstOrDefault(p => string.Equals(p.InternalReference, internalReference, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<PartSummary>> SearchAsync(string? query, int maxResults = 20, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PartSummary>>(_parts.Values
            .Where(p => string.IsNullOrWhiteSpace(query)
                || p.InternalReference.Contains(query, StringComparison.OrdinalIgnoreCase)
                || p.Designation.Contains(query, StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p.InternalReference, StringComparer.Ordinal)
            .Take(maxResults)
            .ToList());

    public Task<IReadOnlyDictionary<Guid, PartSummary>> GetManyAsync(IEnumerable<Guid> ids, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyDictionary<Guid, PartSummary>>(ids.Distinct().Where(_parts.ContainsKey).ToDictionary(id => id, id => _parts[id]));

    public Task<IReadOnlyList<PartRevisionSummary>> GetRevisionsAsync(Guid partId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<PartRevisionSummary>>(_revisions.Values.Where(r => r.PartId == partId).OrderBy(r => r.Index, StringComparer.Ordinal).ToList());

    public Task<RevisionRouting?> GetRoutingAsync(Guid revisionId, CancellationToken cancellationToken = default) =>
        Task.FromResult(_revisions.TryGetValue(revisionId, out var revision)
            ? new RevisionRouting(revision, _routings[revisionId].OrderBy(o => o.Number).ToList())
            : null);
}

/// <summary>Pièce créée dans la doublure : identifiant et indice en vigueur.</summary>
public sealed record FakePart(Guid Id, Guid RevisionId);
