using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Domain;
using Barthazon.Catalog.Core.Persistence;
using Barthazon.Catalog.Core.Resources;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;

namespace Barthazon.Catalog.Core.Services;

/// <summary>
/// Lien vu depuis une fiche. <see cref="IsInverse"/> est vrai quand la fiche est la cible du lien :
/// le libellé affiché est alors le libellé inverse (« Se compose de », « Remplacée par »…).
/// </summary>
public sealed record PartLinkItem(
    Guid Id,
    PartLinkType Type,
    bool IsInverse,
    Guid OtherPartId,
    string OtherReference,
    string OtherDesignation,
    string? OtherIndex,
    PartStatus OtherStatus,
    bool OtherIsArchived,
    decimal? Quantity,
    string? Note);

/// <summary>Ajout d'un lien depuis la fiche <see cref="PartId"/>.</summary>
public sealed class PartLinkInput
{
    public Guid PartId { get; set; }

    public Guid? OtherPartId { get; set; }

    public PartLinkType Type { get; set; } = PartLinkType.Related;

    /// <summary>Vrai : la fiche courante est la cible (ex. « Se compose de » = l'autre pièce fait partie de celle-ci).</summary>
    public bool IsInverse { get; set; }

    public decimal? Quantity { get; set; }

    public string? Note { get; set; }
}

/// <summary>Liens entre pièces (fait partie de, remplace, variante, associée).</summary>
public interface IPartLinkService
{
    Task<Result<IReadOnlyList<PartLinkItem>>> ListAsync(Guid partId, CancellationToken cancellationToken = default);

    Task<Result> AddAsync(PartLinkInput input, CancellationToken cancellationToken = default);

    Task<Result> RemoveAsync(Guid linkId, CancellationToken cancellationToken = default);
}

internal sealed class PartLinkService(IUnitOfWorkFactory<ICatalogUnitOfWork> unitOfWorkFactory, IAccessGuard accessGuard) : IPartLinkService
{
    public async Task<Result<IReadOnlyList<PartLinkItem>>> ListAsync(Guid partId, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanRead();
        if (allowed.IsFailure)
        {
            return Result<IReadOnlyList<PartLinkItem>>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var links = await uow.Links.ListForPartAsync(partId, cancellationToken);
        if (links.Count == 0)
        {
            return Result.Success<IReadOnlyList<PartLinkItem>>([]);
        }

        var otherIds = links.Select(l => l.SourcePartId == partId ? l.TargetPartId : l.SourcePartId).ToHashSet();
        var others = (await uow.Parts.SearchAsync(new PartQuery { Ids = otherIds, IncludeArchived = true }, cancellationToken)).ToDictionary(r => r.Id);
        var items = new List<PartLinkItem>();
        foreach (var link in links)
        {
            var inverse = link.TargetPartId == partId;
            if (others.TryGetValue(inverse ? link.SourcePartId : link.TargetPartId, out var other))
            {
                items.Add(new PartLinkItem(
                    link.Id, link.Type, inverse, other.Id, other.InternalReference, other.Designation, other.CurrentIndex,
                    other.Status, other.IsArchived, link.Quantity, link.Note));
            }
        }

        return Result.Success<IReadOnlyList<PartLinkItem>>(items
            .OrderBy(i => i.Type)
            .ThenBy(i => i.IsInverse)
            .ThenBy(i => i.OtherReference, StringComparer.CurrentCultureIgnoreCase)
            .ToList());
    }

    public async Task<Result> AddAsync(PartLinkInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        var note = Inputs.Clean(input.Note);
        var validation = new Validation()
            .When(input.OtherPartId is null, nameof(PartLinkInput.OtherPartId), CatalogMessages.LinkTargetRequired)
            .When(input.OtherPartId == input.PartId, nameof(PartLinkInput.OtherPartId), CatalogMessages.LinkToItself)
            .When(input.Quantity is <= 0, nameof(PartLinkInput.Quantity), CatalogMessages.QuantityInvalid)
            .When(note?.Length > 500, nameof(PartLinkInput.Note), CatalogMessages.LinkNoteTooLong)
            .When(!Enum.IsDefined(input.Type), nameof(PartLinkInput.Type), CatalogMessages.LinkNotFound);
        if (!validation.IsValid)
        {
            return Result.Failure(validation.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        if (await uow.Parts.GetByIdAsync(input.PartId, cancellationToken) is null || await uow.Parts.GetByIdAsync(input.OtherPartId!.Value, cancellationToken) is null)
        {
            return Result.Failure(Error.NotFound(CatalogMessages.PartNotFound));
        }

        var (source, target) = input.IsInverse ? (input.OtherPartId!.Value, input.PartId) : (input.PartId, input.OtherPartId!.Value);

        // « Associée à » est symétrique : le lien dans l'autre sens compte comme un doublon.
        var exists = await uow.Links.ExistsAsync(source, target, input.Type, cancellationToken)
            || (input.Type == PartLinkType.Related && await uow.Links.ExistsAsync(target, source, input.Type, cancellationToken));
        if (exists)
        {
            return Result.Failure(Error.Conflict(CatalogMessages.LinkExists));
        }

        uow.Links.Add(new PartLink(source, target, input.Type) { Quantity = input.Quantity, Note = note });
        return await uow.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result> RemoveAsync(Guid linkId, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        await using var uow = unitOfWorkFactory.Create();
        var link = await uow.Links.GetByIdAsync(linkId, cancellationToken);
        if (link is null)
        {
            return Result.Failure(Error.NotFound(CatalogMessages.LinkNotFound));
        }

        uow.Links.Remove(link);
        return await uow.SaveChangesAsync(cancellationToken);
    }
}
