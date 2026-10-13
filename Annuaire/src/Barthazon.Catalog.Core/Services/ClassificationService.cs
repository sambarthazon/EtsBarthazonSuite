using System.Globalization;
using Barthazon.Catalog.Core.Domain;
using Barthazon.Catalog.Core.Persistence;
using Barthazon.Catalog.Core.Resources;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;

namespace Barthazon.Catalog.Core.Services;

/// <summary>Familles et étiquettes de pièces.</summary>
public interface IClassificationService
{
    Task<Result<IReadOnlyList<FamilyItem>>> ListFamiliesAsync(bool includeArchived, CancellationToken cancellationToken = default);

    Task<Result<FamilyItem>> SaveFamilyAsync(Guid? id, FamilyInput input, CancellationToken cancellationToken = default);

    Task<Result> SetFamilyArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default);

    Task<Result> DeleteFamilyAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<IReadOnlyList<TagItem>>> ListTagsAsync(bool includeArchived, CancellationToken cancellationToken = default);

    Task<Result<TagItem>> SaveTagAsync(Guid? id, TagInput input, CancellationToken cancellationToken = default);

    Task<Result> SetTagArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default);

    Task<Result> DeleteTagAsync(Guid id, CancellationToken cancellationToken = default);
}

internal sealed class ClassificationService(IUnitOfWorkFactory<ICatalogUnitOfWork> unitOfWorkFactory, IAccessGuard accessGuard, CatalogReferentialObserver reindexer) : IClassificationService
{
    public async Task<Result<IReadOnlyList<FamilyItem>>> ListFamiliesAsync(bool includeArchived, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanRead();
        if (allowed.IsFailure)
        {
            return Result<IReadOnlyList<FamilyItem>>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var families = await uow.Families.ListAsync(includeArchived, cancellationToken);
        var usages = await uow.Parts.CountByFamilyAsync(cancellationToken);
        return Result.Success<IReadOnlyList<FamilyItem>>(families
            .OrderBy(f => f.Name, StringComparer.CurrentCultureIgnoreCase)
            .Select(f => new FamilyItem(f.Id, f.Name, f.Description, f.IsArchived, f.Version, usages.GetValueOrDefault(f.Id)))
            .ToList());
    }

    public async Task<Result<FamilyItem>> SaveFamilyAsync(Guid? id, FamilyInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<FamilyItem>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var validation = new Validation()
            .Required(input.Name, nameof(FamilyInput.Name), CatalogMessages.FamilyNameRequired)
            .MaxLength(input.Name, 100, nameof(FamilyInput.Name), CatalogMessages.TextTooLong)
            .MaxLength(input.Description, 1000, nameof(FamilyInput.Description), CatalogMessages.TextTooLong);
        if (validation.IsValid && await uow.Families.NameExistsAsync(input.Name.Trim(), id, cancellationToken))
        {
            validation.Add(nameof(FamilyInput.Name), CatalogMessages.FamilyNameExists);
        }

        if (!validation.IsValid)
        {
            return Result<FamilyItem>.Failure(validation.Errors);
        }

        PartFamily? family;
        if (id is null)
        {
            family = new PartFamily(input.Name);
            uow.Families.Add(family);
        }
        else
        {
            family = await uow.Families.GetByIdAsync(id.Value, cancellationToken);
            if (family is null)
            {
                return Error.NotFound(CatalogMessages.FamilyNotFound);
            }

            uow.Families.SetExpectedVersion(family, input.Version);
            family.Name = input.Name.Trim();
        }

        family.Description = Inputs.Clean(input.Description);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        if (saved.IsSuccess && id is not null)
        {
            await reindexer.ReindexAsync(familyId: family.Id, cancellationToken: cancellationToken);
        }

        return saved.IsSuccess
            ? new FamilyItem(family.Id, family.Name, family.Description, family.IsArchived, family.Version, 0)
            : Result<FamilyItem>.Failure(saved.Errors);
    }

    public async Task<Result> SetFamilyArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        await using var uow = unitOfWorkFactory.Create();
        var family = await uow.Families.GetByIdAsync(id, cancellationToken);
        if (family is null)
        {
            return Result.Failure(Error.NotFound(CatalogMessages.FamilyNotFound));
        }

        if (archived)
        {
            family.Archive();
        }
        else
        {
            family.Restore();
        }

        return await uow.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result> DeleteFamilyAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        await using var uow = unitOfWorkFactory.Create();
        var usages = await uow.Parts.CountByFamilyAsync(cancellationToken);
        if (usages.GetValueOrDefault(id) is > 0 and var count)
        {
            return Result.Failure(Error.Conflict(string.Format(CultureInfo.CurrentCulture, CatalogMessages.ItemInUse, count)));
        }

        var family = await uow.Families.GetByIdAsync(id, cancellationToken);
        if (family is null)
        {
            return Result.Failure(Error.NotFound(CatalogMessages.FamilyNotFound));
        }

        uow.Families.Remove(family);
        return await uow.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result<IReadOnlyList<TagItem>>> ListTagsAsync(bool includeArchived, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanRead();
        if (allowed.IsFailure)
        {
            return Result<IReadOnlyList<TagItem>>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var tags = await uow.Tags.ListAsync(includeArchived, cancellationToken);
        var usages = await uow.Parts.CountByTagAsync(cancellationToken);
        return Result.Success<IReadOnlyList<TagItem>>(tags
            .OrderBy(t => t.Label, StringComparer.CurrentCultureIgnoreCase)
            .Select(t => new TagItem(t.Id, t.Label, t.Color, t.IsArchived, t.Version, usages.GetValueOrDefault(t.Id)))
            .ToList());
    }

    public async Task<Result<TagItem>> SaveTagAsync(Guid? id, TagInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<TagItem>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var validation = new Validation()
            .Required(input.Label, nameof(TagInput.Label), CatalogMessages.TagLabelRequired)
            .MaxLength(input.Label, 50, nameof(TagInput.Label), CatalogMessages.TextTooLong);
        if (validation.IsValid && await uow.Tags.LabelExistsAsync(input.Label.Trim(), id, cancellationToken))
        {
            validation.Add(nameof(TagInput.Label), CatalogMessages.TagLabelExists);
        }

        if (!validation.IsValid)
        {
            return Result<TagItem>.Failure(validation.Errors);
        }

        Tag? tag;
        if (id is null)
        {
            tag = new Tag(input.Label, input.Color);
            uow.Tags.Add(tag);
        }
        else
        {
            tag = await uow.Tags.GetByIdAsync(id.Value, cancellationToken);
            if (tag is null)
            {
                return Error.NotFound(CatalogMessages.TagNotFound);
            }

            uow.Tags.SetExpectedVersion(tag, input.Version);
            tag.Label = input.Label.Trim();
            tag.Color = input.Color;
        }

        var saved = await uow.SaveChangesAsync(cancellationToken);
        if (saved.IsSuccess && id is not null)
        {
            await reindexer.ReindexAsync(tagId: tag.Id, cancellationToken: cancellationToken);
        }

        return saved.IsSuccess ? new TagItem(tag.Id, tag.Label, tag.Color, tag.IsArchived, tag.Version, 0) : Result<TagItem>.Failure(saved.Errors);
    }

    public async Task<Result> SetTagArchivedAsync(Guid id, bool archived, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        await using var uow = unitOfWorkFactory.Create();
        var tag = await uow.Tags.GetByIdAsync(id, cancellationToken);
        if (tag is null)
        {
            return Result.Failure(Error.NotFound(CatalogMessages.TagNotFound));
        }

        if (archived)
        {
            tag.Archive();
        }
        else
        {
            tag.Restore();
        }

        return await uow.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result> DeleteTagAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        await using var uow = unitOfWorkFactory.Create();
        var usages = await uow.Parts.CountByTagAsync(cancellationToken);
        if (usages.GetValueOrDefault(id) is > 0 and var count)
        {
            return Result.Failure(Error.Conflict(string.Format(CultureInfo.CurrentCulture, CatalogMessages.ItemInUse, count)));
        }

        var tag = await uow.Tags.GetByIdAsync(id, cancellationToken);
        if (tag is null)
        {
            return Result.Failure(Error.NotFound(CatalogMessages.TagNotFound));
        }

        uow.Tags.Remove(tag);
        return await uow.SaveChangesAsync(cancellationToken);
    }
}
