using Barthazon.Catalog.Core.Services;
using Barthazon.Shared.Kernel.Results;
using Microsoft.Net.Http.Headers;

namespace Barthazon.Portal.Endpoints;

/// <summary>
/// Point d'accès contrôlé aux fichiers (jamais de dossier statique public) : droits vérifiés par le service,
/// aperçu intégré seulement pour les PDF et images, téléchargement avec un type de contenu sûr sinon.
/// </summary>
internal static class FileEndpoints
{
    public static void MapFileEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/fichiers/{id:guid}", async (Guid id, string? apercu, HttpContext context, IDocumentService documents) =>
        {
            var result = await documents.OpenAsync(id, context.RequestAborted);
            if (result.IsFailure)
            {
                return result.HasError(ErrorKind.Unauthenticated)
                    ? Results.Redirect("/connexion?returnUrl=" + Uri.EscapeDataString(context.Request.Path + context.Request.QueryString))
                    : Results.NotFound();
            }

            var file = result.Value;
            var inline = apercu is "1" or "true" && DocumentContentTypes.IsPreviewable(file.ContentType);
            var contentType = DocumentContentTypes.IsPreviewable(file.ContentType) || file.ContentType == "text/plain"
                ? file.ContentType
                : DocumentContentTypes.OctetStream;
            var headers = context.Response.Headers;
            headers.XContentTypeOptions = "nosniff";
            headers.CacheControl = "private, max-age=300";
            var disposition = new ContentDispositionHeaderValue(inline ? "inline" : "attachment");
            disposition.SetHttpFileName(file.FileName);
            headers.ContentDisposition = disposition.ToString();
            if (!DocumentContentTypes.IsPdf(file.ContentType))
            {
                // Aucun script ne peut s'exécuter dans un fichier servi (SVG, HTML renommé…).
                headers.ContentSecurityPolicy = "sandbox; default-src 'none'; img-src 'self'; style-src 'unsafe-inline'";
            }

            return Results.Stream(file.Content, contentType, enableRangeProcessing: true);
        });
    }
}
