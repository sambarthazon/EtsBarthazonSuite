using Barthazon.Catalog.Contracts;
using Barthazon.Referential.Contracts;
using Barthazon.Stock.Core.Domain;
using Barthazon.Stock.Core.Services;
using Microsoft.Extensions.Logging;

namespace Barthazon.Stock.Core.Demo;

/// <summary>
/// Données de démonstration du stock (<c>Seed:DemoData</c>), chargées une seule fois si le stock est vide, après
/// celles de l'annuaire : emplacements, une dizaine d'articles (dont un brut commun à deux pièces), des pièces suivies
/// avec seuils et sources (dont un semi-fini commun), des lancements à différentes étapes, un lancement terminé, une
/// sortie, des pièces finies à un indice remplacé. Tout passe par les services applicatifs ; les pièces de l'annuaire
/// sont retrouvées par leur référence et ignorées sans erreur si elles n'existent pas.
/// </summary>
public sealed partial class StockDemoData(
    ILocationService locations,
    IItemService items,
    IMovementService movements,
    ITrackedPartService trackedParts,
    IBatchService batches,
    IFinishedPartService finishedParts,
    IPartLookup parts,
    IPartRoutingLookup routings,
    IMaterialLookup materials,
    ICustomerLookup customers,
    TimeProvider timeProvider,
    ILogger<StockDemoData> logger)
{
    private DateTimeOffset _now;

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var existing = await items.SearchAsync(new ItemSearchRequest { IncludeArchived = true }, cancellationToken);
        if (existing.IsFailure || existing.Value.Count > 0)
        {
            return;
        }

        LogSeeding(logger);
        _now = timeProvider.GetUtcNow();

        // Emplacements (MAG, ATELIER et EXPE existent dès l'installation).
        var all = (await locations.ListAsync(includeArchived: false, cancellationToken)).Value;
        Guid Location(string code) => all.First(l => l.Code == code).Id;
        async Task AddLocation(string code, string name, StockLocationKind kind, int order)
        {
            var saved = await locations.SaveAsync(null, new LocationInput { Code = code, Name = name, Kind = kind, SortOrder = order }, cancellationToken);
            if (saved.IsSuccess)
            {
                all = [.. all, saved.Value];
            }
        }

        await AddLocation("RAYON-A", "Rayonnage barres acier", StockLocationKind.Storage, 2);
        await AddLocation("RAYON-B", "Rayonnage alu, inox, plastiques", StockLocationKind.Storage, 3);
        await AddLocation("TTS", "Traitements du Forez (sous-traitant)", StockLocationKind.Subcontractor, 20);

        // Articles : matières et bruts, quelques-uns fournis par le client.
        var materialList = await materials.ListAsync(includeArchived: false, cancellationToken);
        Guid? Material(string designation) => materialList.FirstOrDefault(m => m.Designation == designation)?.Id;
        var hydrau = await customers.GetByCodeAsync("HYDRAU", cancellationToken);
        var created = new Dictionary<string, Guid>();
        async Task Item(string code, string designation, StockItemKind kind, string? material, BlankShape? shape, string? dimensions, StockUnit unit,
            decimal? threshold, string location, decimal? cost, Guid? owner = null, bool lots = false)
        {
            var saved = await items.SaveAsync(null, new ItemInput
            {
                Code = code, Designation = designation, Kind = kind, MaterialId = material is null ? null : Material(material), Shape = shape,
                Dimensions = dimensions, Unit = unit, MinimumThreshold = threshold, DefaultLocationId = Location(location), ReferenceUnitCost = cost,
                OwnerCustomerId = owner, TrackLots = lots, UsualSupplier = owner is null ? "Aciers du Centre" : null,
            }, cancellationToken);
            if (saved.IsSuccess)
            {
                created[code] = saved.Value.Id;
            }
        }

        await Item("A-0001", "Rond C45 Ø25", StockItemKind.RawMaterial, "Acier C45", BlankShape.Round, "Ø25", StockUnit.Meter, 6, "RAYON-A", 4.2m, lots: true);
        await Item("A-0002", "Rond 42CrMo4 Ø50", StockItemKind.RawMaterial, "Acier 42CrMo4", BlankShape.Round, "Ø50", StockUnit.Meter, 4, "RAYON-A", 14.8m, lots: true);
        await Item("A-0003", "Tube alu 6082 Ø30/Ø12", StockItemKind.RawMaterial, "Aluminium 6082", BlankShape.Tube, "Ø30/Ø12", StockUnit.Meter, 4, "RAYON-B", 9.6m);
        await Item("A-0004", "Tube bronze Ø70/Ø40", StockItemKind.RawMaterial, "Bronze", BlankShape.Tube, "Ø70/Ø40", StockUnit.Meter, 3, "RAYON-B", 68m);
        await Item("A-0005", "Rond inox 304L Ø35", StockItemKind.RawMaterial, "Inox 304L", BlankShape.Round, "Ø35", StockUnit.Meter, 2, "RAYON-B", 22.5m);
        await Item("A-0006", "Bloc S235 120 × 80 × 60", StockItemKind.Blank, "Acier S235", BlankShape.Block, "120 × 80 × 60", StockUnit.Piece, 10, "MAG", 6.4m);
        await Item("A-0007", "Débit C45 Ø45 × 315", StockItemKind.Blank, "Acier C45", BlankShape.Round, "Ø45 × 315", StockUnit.Piece, 5, "MAG", 3.9m);
        await Item("A-0008", "Rond PTFE Ø40 (client)", StockItemKind.RawMaterial, "PTFE", BlankShape.Round, "Ø40", StockUnit.Meter, null, "RAYON-B", null, hydrau?.Id);
        await Item("A-0009", "Rond titane TA6V Ø12 (client)", StockItemKind.RawMaterial, "Titane TA6V", BlankShape.Round, "Ø12", StockUnit.Meter, null, "RAYON-B", null, hydrau?.Id);
        await Item("A-0010", "Plat alu 2017A 100 × 15", StockItemKind.RawMaterial, "Aluminium 2017A", BlankShape.Flat, "100 × 15", StockUnit.Meter, 2, "RAYON-B", 31m);
        await Item("A-0011", "Huile de coupe soluble", StockItemKind.Other, null, null, "Bidon 20 L", StockUnit.Piece, 2, "MAG", 74m);

        async Task Receive(string code, decimal quantity, int daysAgo, string? lot = null, string? reference = null)
        {
            if (created.TryGetValue(code, out var id))
            {
                await movements.ReceiveItemAsync(new ItemMovementInput
                {
                    ItemId = id, Quantity = quantity, Lot = lot, Reference = reference ?? "BL fournisseur", OccurredAt = Ago(daysAgo),
                }, cancellationToken);
            }
        }

        await Receive("A-0001", 18, 20, "C-2026-112", "BL 45871");
        await Receive("A-0002", 6, 21, "C-2026-097", "BL 45790");
        await Receive("A-0002", 6, 9, "C-2026-131", "BL 46022");
        await Receive("A-0003", 12, 18);
        await Receive("A-0004", 2.2m, 25);
        await Receive("A-0005", 6, 15);
        await Receive("A-0006", 24, 14);
        await Receive("A-0007", 15, 12);
        await Receive("A-0008", 3, 10, reference: "BL HYDRAU 2026-77");
        await Receive("A-0009", 1.2m, 10, reference: "BL HYDRAU 2026-77");
        await Receive("A-0011", 3, 30);

        // Pièces suivies : seuils, rangement, sources de brut (brut commun A-0003, semi-fini commun P-0001 après l'op 20).
        var found = new Dictionary<string, PartSummary>();
        foreach (var reference in new[] { "P-0001", "P-0002", "P-0004", "P-0005", "P-0006", "P-0007", "P-0008", "P-0009" })
        {
            if (await parts.GetByReferenceAsync(reference, cancellationToken) is { } part)
            {
                found[reference] = part;
            }
        }

        async Task Track(string reference, int threshold, string location, params BlankSourceInput[] sources)
        {
            if (!found.TryGetValue(reference, out var part))
            {
                return;
            }

            var tracked = await trackedParts.TrackAsync(part.Id, cancellationToken);
            if (tracked.IsFailure)
            {
                return;
            }

            await trackedParts.SaveSettingsAsync(part.Id, new TrackedPartSettingsInput
            {
                MinimumThreshold = threshold, DefaultLocationId = Location(location), Version = tracked.Value.Version,
            }, cancellationToken);
            foreach (var source in sources)
            {
                await trackedParts.SaveSourceAsync(part.Id, null, source, cancellationToken);
            }
        }

        BlankSourceInput FromItem(string code, decimal perPart, bool isDefault = true) =>
            new() { Kind = BlankSourceKind.Item, ItemId = created.GetValueOrDefault(code), QuantityPerPart = perPart, IsDefault = isDefault };

        await Track("P-0001", 10, "MAG", FromItem("A-0002", 0.423m));
        await Track("P-0002", 5, "MAG", FromItem("A-0004", 0.058m));
        await Track("P-0004", 20, "MAG", FromItem("A-0001", 0.098m));
        await Track("P-0005", 4, "MAG", FromItem("A-0005", 0.163m));
        await Track("P-0006", 40, "MAG", FromItem("A-0003", 0.013m));
        await Track("P-0007", 40, "MAG", FromItem("A-0003", 0.028m));
        await Track("P-0008", 6, "MAG", FromItem("A-0006", 1));
        if (found.TryGetValue("P-0001", out var shaft))
        {
            await Track("P-0009", 2, "MAG", new BlankSourceInput
            {
                Kind = BlankSourceKind.Part, SourcePartId = shaft.Id, SourceAfterOperation = 20, QuantityPerPart = 1, IsDefault = true,
                Note = "Arbre P-0001 ébauché, repris pour l'arbre intermédiaire",
            }, FromItem("A-0007", 1, isDefault: false));
        }

        // Pièces finies à un indice remplacé (P-0001 indice B) et retour client à l'indice en vigueur.
        if (found.TryGetValue("P-0001", out shaft))
        {
            var revisions = await routings.GetRevisionsAsync(shaft.Id, cancellationToken);
            if (revisions.FirstOrDefault(r => r.Index == "B") is { } indexB)
            {
                await finishedParts.ReceiveAsync(new PartReceiptInput
                {
                    PartId = shaft.Id, RevisionId = indexB.Id, Quantity = 6, Lot = "Stock 2025", Comment = "Reprise du stock existant", OccurredAt = Ago(28),
                }, cancellationToken);
            }

            await finishedParts.ReceiveAsync(new PartReceiptInput
            {
                PartId = shaft.Id, Quantity = 2, Lot = "RETOUR", Reference = "Retour DUPONT", OccurredAt = Ago(6),
            }, cancellationToken);
        }

        // Lancements à différentes étapes.
        var arbre = await Launch("P-0001", 14, 8, "CDE DUPONT 2026-118");
        await Declare(arbre, 1, 14, 0, 7);
        await Declare(arbre, 2, 9, 1, 5);

        var entretoise10 = await Launch("P-0006", 50, 11, "CDE AGRITEC 7781");
        await Declare(entretoise10, 1, 50, 0, 10);
        await Declare(entretoise10, 2, 50, 0, 9);
        await Declare(entretoise10, 3, 50, 0, 8);
        await Declare(entretoise10, 4, 48, 2, 7);
        if (found.TryGetValue("P-0006", out var spacer))
        {
            await finishedParts.ShipAsync(new PartShipmentInput { PartId = spacer.Id, Quantity = 20, Reference = "BL 2026-0412", OccurredAt = Ago(5) }, cancellationToken);
        }

        var entretoise25 = await Launch("P-0007", 30, 4, "CDE AGRITEC 7790");
        await Declare(entretoise25, 1, 30, 0, 3);

        var support = await Launch("P-0008", 10, 3, null);
        await Declare(support, 1, 10, 0, 2);
        await Declare(support, 2, 4, 0, 1);

        await Launch("P-0002", 20, 1, "CDE MECALOIRE 3310");
        LogSeeded(logger, created.Count, found.Count);
    }

    private DateTimeOffset Ago(int days) => _now.AddDays(-days);

    /// <summary>Lancement avec la source par défaut, consommée case par case (lot le plus ancien d'abord).</summary>
    private async Task<BatchDetails?> Launch(string reference, int quantity, int daysAgo, string? order)
    {
        if (await parts.GetByReferenceAsync(reference) is not { } part)
        {
            return null;
        }

        var preview = await batches.PreviewLaunchAsync(part.Id, null, quantity);
        if (preview.IsFailure)
        {
            return null;
        }

        var input = new LaunchInput
        {
            PartId = part.Id, Quantity = quantity, CustomerOrderReference = order, OccurredAt = Ago(daysAgo),
            DueDate = DateOnly.FromDateTime(Ago(daysAgo - 21).Date),
        };
        if (preview.Value.Sources.FirstOrDefault() is { } source)
        {
            input.SourceId = source.Source.Id;
            var remaining = source.ProposedQuantity;
            foreach (var cell in source.Cells.Where(c => c.Quantity > 0))
            {
                var taken = Math.Min(remaining, cell.Quantity);
                input.Consumptions.Add(new ConsumptionInput { Cell = cell.Key, Quantity = taken });
                remaining -= taken;
                if (remaining <= 0)
                {
                    break;
                }
            }
        }
        else
        {
            input.WithoutConsumption = true;
        }

        var launched = await batches.LaunchAsync(input);
        return launched.IsSuccess ? launched.Value : null;
    }

    private async Task Declare(BatchDetails? batch, int stepSequence, int good, int scrap, int daysAgo)
    {
        if (batch is null)
        {
            return;
        }

        var result = await batches.DeclareAsync(new DeclareInput
        {
            BatchId = batch.Id, StepSequence = stepSequence, GoodQuantity = good, ScrapQuantity = scrap,
            ScrapReason = scrap > 0 ? StockReason.Dimension : null, OccurredAt = Ago(daysAgo),
        });
        if (result.IsFailure)
        {
            LogDeclarationFailed(logger, batch.Number, stepSequence);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Chargement des données de démonstration du stock…")]
    private static partial void LogSeeding(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Données de démonstration du stock chargées : {Items} articles, {Parts} pièces suivies.")]
    private static partial void LogSeeded(ILogger logger, int items, int parts);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Démonstration du stock : déclaration impossible ({Batch}, étape {Step}).")]
    private static partial void LogDeclarationFailed(ILogger logger, string batch, int step);
}
