using Barthazon.Catalog.Core.Domain;
using Barthazon.Catalog.Core.Persistence;
using Barthazon.Referential.Contracts;
using Barthazon.Shared.Kernel.Text;

namespace Barthazon.Catalog.Core.Services;

/// <summary>
/// Calcule le champ de recherche normalisé d'une pièce : référence interne, désignation, référence client,
/// code et nom du client, matière, famille, étiquettes et indices.
/// </summary>
internal sealed class PartSearchIndexer(ICustomerLookup customers, IMaterialLookup materials)
{
    public async Task RefreshAsync(ICatalogUnitOfWork uow, IReadOnlyCollection<Part> parts, CancellationToken cancellationToken)
    {
        if (parts.Count == 0)
        {
            return;
        }

        var customerById = await customers.GetManyAsync(parts.Select(p => p.CustomerId), cancellationToken);
        var materialIds = parts.SelectMany(p => p.Revisions).Select(r => r.MaterialId).OfType<Guid>();
        var materialById = await materials.GetManyAsync(materialIds, cancellationToken);
        var families = (await uow.Families.ListAsync(includeArchived: true, cancellationToken)).ToDictionary(f => f.Id, f => f.Name);
        var tags = (await uow.Tags.ListAsync(includeArchived: true, cancellationToken)).ToDictionary(t => t.Id, t => t.Label);

        foreach (var part in parts)
        {
            part.RefreshCurrentRevision();
            var values = new List<string?>
            {
                part.InternalReference,
                part.Designation,
                part.CustomerReference,
            };
            if (customerById.TryGetValue(part.CustomerId, out var customer))
            {
                values.Add(customer.Code);
                values.Add(customer.Name);
            }

            if (part.CurrentRevision?.MaterialId is { } materialId && materialById.TryGetValue(materialId, out var material))
            {
                values.Add(material.Designation);
                values.Add(material.Grade);
            }

            if (part.FamilyId is { } familyId && families.TryGetValue(familyId, out var familyName))
            {
                values.Add(familyName);
            }

            values.AddRange(part.Tags.Select(t => tags.GetValueOrDefault(t.TagId)));
            values.AddRange(part.Revisions.Select(r => r.Index));
            part.SearchText = SearchNormalizer.BuildSearchText(values);
        }
    }
}
