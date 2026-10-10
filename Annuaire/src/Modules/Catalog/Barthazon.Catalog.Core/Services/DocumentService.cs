using System.Globalization;
using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Domain;
using Barthazon.Catalog.Core.Persistence;
using Barthazon.Catalog.Core.Resources;
using Barthazon.Shared.Kernel.Auditing;
using Barthazon.Shared.Kernel.Files;
using Barthazon.Shared.Kernel.Persistence;
using Barthazon.Shared.Kernel.Results;
using Barthazon.Shared.Kernel.Security;

namespace Barthazon.Catalog.Core.Services;

/// <summary>Ajout d'un fichier.</summary>
public sealed class FileDocumentInput
{
    public string Title { get; set; } = string.Empty;

    public DocumentType Type { get; set; } = DocumentType.Drawing;

    public string? Notes { get; set; }
}

/// <summary>Référence à un document papier.</summary>
public sealed class PaperDocumentInput
{
    public string Title { get; set; } = string.Empty;

    public DocumentType Type { get; set; } = DocumentType.Drawing;

    public string? PaperLocation { get; set; }

    public string? Notes { get; set; }
}

/// <summary>Contenu d'un document à servir (flux à fermer par l'appelant).</summary>
public sealed record DocumentContent(Stream Content, string FileName, string ContentType, long? Length);

/// <summary>Plans et documents : envoi en flux, papier, aperçu, téléchargement.</summary>
public interface IDocumentService
{
    Task<Result<DocumentDto>> AddFileAsync(Guid partId, Guid revisionId, FileDocumentInput input, string fileName, Stream content, CancellationToken cancellationToken = default);

    Task<Result<DocumentDto>> AddPaperAsync(Guid partId, Guid revisionId, PaperDocumentInput input, CancellationToken cancellationToken = default);

    /// <summary>Remplace un document papier par son scan ou sa photo (le document devient numérique).</summary>
    Task<Result<DocumentDto>> AttachFileToPaperAsync(Guid partId, Guid documentId, string fileName, Stream content, CancellationToken cancellationToken = default);

    Task<Result<DocumentDto>> UpdateAsync(Guid partId, Guid documentId, PaperDocumentInput input, CancellationToken cancellationToken = default);

    Task<Result> RemoveAsync(Guid partId, Guid documentId, CancellationToken cancellationToken = default);

    /// <summary>Choisit la photo affichée en vignette (null = aucune).</summary>
    Task<Result> SetMainPhotoAsync(Guid partId, Guid? documentId, CancellationToken cancellationToken = default);

    /// <summary>Ouvre le fichier d'un document (contrôle du droit de lecture).</summary>
    Task<Result<DocumentContent>> OpenAsync(Guid documentId, CancellationToken cancellationToken = default);
}

internal sealed class DocumentService(
    IUnitOfWorkFactory<ICatalogUnitOfWork> unitOfWorkFactory,
    IAccessGuard accessGuard,
    IFileStorage storage,
    FileStorageLimits limits) : IDocumentService
{
    public async Task<Result<DocumentDto>> AddFileAsync(Guid partId, Guid revisionId, FileDocumentInput input, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(content);
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<DocumentDto>.Failure(allowed.Errors);
        }

        var title = string.IsNullOrWhiteSpace(input.Title) ? Path.GetFileNameWithoutExtension(fileName) : input.Title;
        var validation = new Validation()
            .Required(title, nameof(FileDocumentInput.Title), CatalogMessages.DocumentTitleRequired)
            .MaxLength(title, 200, nameof(FileDocumentInput.Title), CatalogMessages.TextTooLong)
            .Required(fileName, "File", CatalogMessages.FileRequired)
            .MaxLength(input.Notes, 2000, nameof(FileDocumentInput.Notes), CatalogMessages.TextTooLong);
        if (!validation.IsValid)
        {
            return Result<DocumentDto>.Failure(validation.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var part = await uow.Parts.GetAggregateAsync(partId, cancellationToken);
        var revision = part?.FindRevision(revisionId);
        if (part is null || revision is null)
        {
            return Error.NotFound(CatalogMessages.RevisionNotFound);
        }

        var document = revision.AddDocument(DocumentKind.File, input.Type, title);
        document.Notes = Inputs.Clean(input.Notes);
        var stored = await StoreAsync(part, revision, document, fileName, content, cancellationToken);
        if (stored.IsFailure)
        {
            return Result<DocumentDto>.Failure(stored.Errors);
        }

        if (input.Type == DocumentType.Photo && part.MainPhotoDocumentId is null && DocumentContentTypes.IsImage(document.ContentType))
        {
            part.MainPhotoDocumentId = document.Id;
        }

        uow.AddAuditNote(nameof(PartDocument), document.Id, AuditActions.DocumentAdded, $"{revision.Index} · {document.Title} ({document.FileName})", nameof(Part), part.Id);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            await storage.DeleteAsync(document.StorageKey!, cancellationToken);
            return Result<DocumentDto>.Failure(saved.Errors);
        }

        return PartService.ToDocumentDto(document);
    }

    public async Task<Result<DocumentDto>> AddPaperAsync(Guid partId, Guid revisionId, PaperDocumentInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<DocumentDto>.Failure(allowed.Errors);
        }

        var validation = ValidatePaper(input, requireLocation: true);
        if (!validation.IsValid)
        {
            return Result<DocumentDto>.Failure(validation.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var part = await uow.Parts.GetAggregateAsync(partId, cancellationToken);
        var revision = part?.FindRevision(revisionId);
        if (part is null || revision is null)
        {
            return Error.NotFound(CatalogMessages.RevisionNotFound);
        }

        var document = revision.AddDocument(DocumentKind.Paper, input.Type, input.Title);
        document.PaperLocation = Inputs.Clean(input.PaperLocation);
        document.Notes = Inputs.Clean(input.Notes);
        uow.AddAuditNote(nameof(PartDocument), document.Id, AuditActions.DocumentAdded, $"{revision.Index} · {document.Title} (papier : {document.PaperLocation})", nameof(Part), part.Id);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? PartService.ToDocumentDto(document) : Result<DocumentDto>.Failure(saved.Errors);
    }

    public async Task<Result<DocumentDto>> AttachFileToPaperAsync(Guid partId, Guid documentId, string fileName, Stream content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(content);
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<DocumentDto>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var part = await uow.Parts.GetAggregateAsync(partId, cancellationToken);
        var revision = part?.Revisions.FirstOrDefault(r => r.Documents.Any(d => d.Id == documentId));
        var document = revision?.Documents.First(d => d.Id == documentId);
        if (part is null || revision is null || document is null)
        {
            return Error.NotFound(CatalogMessages.DocumentNotFound);
        }

        var previousKey = document.StorageKey;
        var stored = await StoreAsync(part, revision, document, fileName, content, cancellationToken);
        if (stored.IsFailure)
        {
            return Result<DocumentDto>.Failure(stored.Errors);
        }

        uow.AddAuditNote(nameof(PartDocument), document.Id, AuditActions.DocumentAdded, $"{revision.Index} · {document.Title} : fichier {document.FileName} ajouté", nameof(Part), part.Id);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        if (saved.IsFailure)
        {
            return Result<DocumentDto>.Failure(saved.Errors);
        }

        if (previousKey is not null && previousKey != document.StorageKey && !await uow.Parts.IsStorageKeyUsedAsync(previousKey, document.Id, cancellationToken))
        {
            await storage.DeleteAsync(previousKey, cancellationToken);
        }

        return PartService.ToDocumentDto(document);
    }

    public async Task<Result<DocumentDto>> UpdateAsync(Guid partId, Guid documentId, PaperDocumentInput input, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return Result<DocumentDto>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var part = await uow.Parts.GetAggregateAsync(partId, cancellationToken);
        var document = part?.Revisions.SelectMany(r => r.Documents).FirstOrDefault(d => d.Id == documentId);
        if (document is null)
        {
            return Error.NotFound(CatalogMessages.DocumentNotFound);
        }

        var validation = ValidatePaper(input, requireLocation: document.Kind == DocumentKind.Paper);
        if (!validation.IsValid)
        {
            return Result<DocumentDto>.Failure(validation.Errors);
        }

        document.Title = input.Title.Trim();
        document.Type = input.Type;
        document.PaperLocation = Inputs.Clean(input.PaperLocation);
        document.Notes = Inputs.Clean(input.Notes);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        return saved.IsSuccess ? PartService.ToDocumentDto(document) : Result<DocumentDto>.Failure(saved.Errors);
    }

    public async Task<Result> RemoveAsync(Guid partId, Guid documentId, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        await using var uow = unitOfWorkFactory.Create();
        var part = await uow.Parts.GetAggregateAsync(partId, cancellationToken);
        var revision = part?.Revisions.FirstOrDefault(r => r.Documents.Any(d => d.Id == documentId));
        var document = revision?.Documents.First(d => d.Id == documentId);
        if (part is null || revision is null || document is null)
        {
            return Result.Failure(Error.NotFound(CatalogMessages.DocumentNotFound));
        }

        revision.RemoveDocument(document);
        if (part.MainPhotoDocumentId == documentId)
        {
            part.MainPhotoDocumentId = null;
        }

        uow.AddAuditNote(nameof(PartDocument), document.Id, AuditActions.DocumentRemoved, $"{revision.Index} · {document.Title}", nameof(Part), part.Id);
        var saved = await uow.SaveChangesAsync(cancellationToken);
        if (saved.IsSuccess && document.StorageKey is { } key && !await uow.Parts.IsStorageKeyUsedAsync(key, document.Id, cancellationToken))
        {
            // Le fichier n'est plus référencé par aucun indice : il peut être supprimé du disque.
            await storage.DeleteAsync(key, cancellationToken);
        }

        return saved;
    }

    public async Task<Result> SetMainPhotoAsync(Guid partId, Guid? documentId, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanEdit();
        if (allowed.IsFailure)
        {
            return allowed;
        }

        await using var uow = unitOfWorkFactory.Create();
        var part = await uow.Parts.GetAggregateAsync(partId, cancellationToken);
        if (part is null)
        {
            return Result.Failure(Error.NotFound(CatalogMessages.PartNotFound));
        }

        if (documentId is { } id && !part.Revisions.SelectMany(r => r.Documents).Any(d => d.Id == id && DocumentContentTypes.IsImage(d.ContentType)))
        {
            return Result.Failure(Error.NotFound(CatalogMessages.DocumentNotFound));
        }

        part.MainPhotoDocumentId = documentId;
        return await uow.SaveChangesAsync(cancellationToken);
    }

    public async Task<Result<DocumentContent>> OpenAsync(Guid documentId, CancellationToken cancellationToken = default)
    {
        var allowed = accessGuard.CanRead();
        if (allowed.IsFailure)
        {
            return Result<DocumentContent>.Failure(allowed.Errors);
        }

        await using var uow = unitOfWorkFactory.Create();
        var document = await uow.Parts.GetDocumentAsync(documentId, cancellationToken);
        if (document?.StorageKey is null || !await storage.ExistsAsync(document.StorageKey, cancellationToken))
        {
            return Error.NotFound(CatalogMessages.DocumentNotFound);
        }

        var stream = await storage.OpenReadAsync(document.StorageKey, cancellationToken);
        return new DocumentContent(stream, document.FileName ?? "document", document.ContentType ?? DocumentContentTypes.OctetStream, document.SizeBytes);
    }

    private async Task<Result> StoreAsync(Part part, PartRevision revision, PartDocument document, string fileName, Stream content, CancellationToken cancellationToken)
    {
        var safeName = FileNames.Sanitize(fileName);
        var key = $"parts/{part.Id:N}/{revision.Id:N}/{document.Id:N}_{safeName}";
        try
        {
            var stored = await storage.SaveAsync(key, content, limits.MaxFileSizeBytes, cancellationToken);
            document.AttachFile(Path.GetFileName(fileName), DocumentContentTypes.FromFileName(fileName), stored.SizeBytes, stored.Sha256, stored.StorageKey);
            return Result.Success();
        }
        catch (FileTooLargeException)
        {
            revision.RemoveDocument(document);
            return Result.Failure(Error.Validation(string.Format(CultureInfo.CurrentCulture, CatalogMessages.FileTooLarge, limits.MaxFileSizeMb), "File"));
        }
    }

    private static Validation ValidatePaper(PaperDocumentInput input, bool requireLocation)
    {
        var validation = new Validation()
            .Required(input.Title, nameof(PaperDocumentInput.Title), CatalogMessages.DocumentTitleRequired)
            .MaxLength(input.Title, 200, nameof(PaperDocumentInput.Title), CatalogMessages.TextTooLong)
            .MaxLength(input.PaperLocation, 300, nameof(PaperDocumentInput.PaperLocation), CatalogMessages.TextTooLong)
            .MaxLength(input.Notes, 2000, nameof(PaperDocumentInput.Notes), CatalogMessages.TextTooLong);
        if (requireLocation)
        {
            validation.Required(input.PaperLocation, nameof(PaperDocumentInput.PaperLocation), CatalogMessages.PaperLocationRequired);
        }

        return validation;
    }
}
