using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Domain;
using Barthazon.Catalog.Core.Persistence;
using Barthazon.Shared.Kernel.Persistence;

namespace Barthazon.Catalog.Core.Services;

/// <summary>Contrat <see cref="IPartRoutingLookup"/> : indices et gammes, en lecture seule, pour les autres applications.</summary>
internal sealed class PartRoutingLookup(IUnitOfWorkFactory<ICatalogUnitOfWork> unitOfWorkFactory) : IPartRoutingLookup
{
    public async Task<IReadOnlyList<PartRevisionSummary>> GetRevisionsAsync(Guid partId, CancellationToken cancellationToken = default)
    {
        await using var uow = unitOfWorkFactory.Create();
        var part = await uow.Parts.GetAggregateAsync(partId, cancellationToken);
        return part is null ? [] : part.Revisions.OrderBy(r => r.Sequence).Select(ToSummary).ToList();
    }

    public async Task<RevisionRouting?> GetRoutingAsync(Guid revisionId, CancellationToken cancellationToken = default)
    {
        await using var uow = unitOfWorkFactory.Create();
        var part = await uow.Parts.GetAggregateByRevisionIdAsync(revisionId, cancellationToken);
        var revision = part?.FindRevision(revisionId);
        if (revision is null)
        {
            return null;
        }

        var operations = revision.Operations
            .OrderBy(o => o.Number)
            .Select(o => new RoutingOperationSummary(o.Id, o.Number, o.Type, o.MachineId, o.Description, o.SetupTimeMin, o.CycleTimeMin, o.Subcontractor))
            .ToList();
        return new RevisionRouting(ToSummary(revision), operations);
    }

    private static PartRevisionSummary ToSummary(PartRevision r) =>
        new(r.Id, r.PartId, r.Index, r.Status, r.EffectiveDate, r.MaterialId, r.BlankShape, r.BlankDimensions);
}
