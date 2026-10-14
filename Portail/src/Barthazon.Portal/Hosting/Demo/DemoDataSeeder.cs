using System.Security.Claims;
using Barthazon.Catalog.Contracts;
using Barthazon.Catalog.Core.Services;
using Barthazon.Referential.Contracts;
using Barthazon.Referential.Core.Services;
using Barthazon.Shared.Kernel.Security;
using Barthazon.Stock.Core.Demo;

namespace Barthazon.Portal.Hosting.Demo;

/// <summary>
/// Données de démonstration fictives (Seed:DemoData) : 4 clients, des machines, une quinzaine de pièces
/// avec indices, gamme, plans PDF, photos et documents papier. Chargées une seule fois, si l'annuaire est vide.
/// Elles passent par les services applicatifs (mêmes règles que la saisie). Le stock ajoute ensuite les siennes
/// (<see cref="StockDemoData"/>), si l'application est active et que son stock est vide.
/// </summary>
internal sealed partial class DemoDataSeeder(IServiceScopeFactory scopeFactory, ILogger<DemoDataSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var services = scope.ServiceProvider;
        services.GetRequiredService<CurrentUserAccessor>().SetUser(new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, Guid.Empty.ToString()),
                new Claim(ClaimTypes.Name, "demo"),
                new Claim(BarthazonClaims.DisplayName, "Démonstration"),
                new Claim(ClaimTypes.Role, nameof(UserRole.Administrator)),
            ],
            "demo")));

        var parts = services.GetRequiredService<IPartService>();
        if ((await parts.SearchAsync(new PartSearchRequest { IncludeArchived = true, PageSize = 1 }, cancellationToken)).Value.TotalCount == 0)
        {
            await SeedCatalogAsync(services, parts, cancellationToken);
        }

        // Stock (seulement si l'application est active) : après l'annuaire, dont il retrouve les pièces par leur référence.
        if (services.GetService<StockDemoData>() is { } stock)
        {
            await stock.SeedAsync(cancellationToken);
        }
    }

    private async Task SeedCatalogAsync(IServiceProvider services, IPartService parts, CancellationToken cancellationToken)
    {
        LogSeeding(logger);
        var customers = services.GetRequiredService<ICustomerService>();
        var machines = services.GetRequiredService<IMachineService>();
        var materials = services.GetRequiredService<IMaterialService>();
        var classification = services.GetRequiredService<IClassificationService>();
        var revisions = services.GetRequiredService<IRevisionService>();
        var documents = services.GetRequiredService<IDocumentService>();

        async Task<Guid> Customer(string code, string name, string city, string contact, string function, string phone, string email) =>
            (await customers.CreateAsync(new CustomerInput
            {
                Code = code, Name = name, City = city,
                Contacts = [new ContactInput { Name = contact, Function = function, Phone = phone, Email = email }],
            }, cancellationToken)).Value.Id;

        var dupont = await Customer("DUPONT", "Dupont Énergie", "Saint-Étienne", "Jean Martin", "Acheteur", "04 77 00 00 01", "jean.martin@dupont-energie.example");
        var mecaloire = await Customer("MECALOIRE", "Méca Loire Industrie", "Roanne", "Sophie Bernard", "Responsable méthodes", "04 77 00 00 02", "s.bernard@mecaloire.example");
        var hydrau = await Customer("HYDRAU", "Hydrau-Systèmes", "Lyon", "Karim Haddad", "Bureau d'études", "04 72 00 00 03", "k.haddad@hydrau.example");
        var agritec = await Customer("AGRITEC", "Agritec Machines", "Clermont-Ferrand", "Paul Roux", "Acheteur", "04 73 00 00 04", "paul.roux@agritec.example");

        async Task<Guid> Machine(string name, MachineType type) => (await machines.CreateAsync(new MachineInput { Name = name, Type = type }, cancellationToken)).Value.Id;
        var mazak = await Machine("Tour CN Mazak QT-200", MachineType.CncLathe);
        var ernault = await Machine("Tour conventionnel Ernault", MachineType.ConventionalLathe);
        var haas = await Machine("Centre d'usinage Haas VF-2", MachineType.CncMill);
        var huron = await Machine("Fraiseuse Huron", MachineType.ConventionalMill);
        var saw = await Machine("Scie à ruban Kasto", MachineType.Saw);
        var grinder = await Machine("Rectifieuse cylindrique", MachineType.Grinder);
        var control = await Machine("Poste de contrôle", MachineType.Inspection);

        var materialList = (await materials.ListAsync(false, cancellationToken)).Value;
        Guid Material(string designation) => materialList.First(m => m.Designation == designation).Id;

        async Task<Guid> Family(string name) => (await classification.SaveFamilyAsync(null, new FamilyInput { Name = name }, cancellationToken)).Value.Id;
        var shafts = await Family("Arbres");
        var rings = await Family("Bagues");
        var flanges = await Family("Brides");
        var axles = await Family("Axes");
        var supports = await Family("Supports");
        var spacers = await Family("Entretoises");

        async Task<Guid> Tag(string label, TagColor color) => (await classification.SaveTagAsync(null, new TagInput { Label = label, Color = color }, cancellationToken)).Value.Id;
        var safety = await Tag("Pièce de sécurité", TagColor.Error);
        var customerMaterial = await Tag("Matière client", TagColor.Info);
        var series = await Tag("Série", TagColor.Primary);
        var rework = await Tag("Reprise possible", TagColor.Warning);

        var number = 0;
        async Task<PartDetails> Part(string designation, Guid customer, string customerRef, Guid family, string material, string index, bool inForce,
            BlankShape shape, string blank, Guid[] tags, PartStatus status = PartStatus.Active)
        {
            number++;
            var created = await parts.CreateAsync(new PartCreateInput
            {
                InternalReference = $"P-{number:0000}",
                Designation = designation,
                CustomerId = customer,
                CustomerReference = customerRef,
                FamilyId = family,
                Status = status,
                TagIds = [.. tags],
                FirstIndex = index,
                PutInForce = inForce,
                MaterialId = Material(material),
                BlankShape = shape,
                BlankDimensions = blank,
            }, cancellationToken);
            return created.Value;
        }

        async Task Specs(PartDetails part, Action<RevisionInput> configure, Guid? revisionId = null)
        {
            var id = revisionId ?? part.CurrentRevisionId!.Value;
            var revision = (await parts.GetRevisionAsync(part.Id, id, cancellationToken)).Value;
            var input = RevisionInput.From(revision);
            configure(input);
            await revisions.UpdateAsync(part.Id, id, input, cancellationToken);
        }

        async Task Drawing(PartDetails part, string index, string customer, (double, double)[] steps, Guid? revisionId = null)
        {
            using var pdf = new MemoryStream(DemoFiles.DrawingPdf(part.InternalReference, part.Designation, index, customer, steps));
            await documents.AddFileAsync(part.Id, revisionId ?? part.CurrentRevisionId!.Value,
                new FileDocumentInput { Title = $"Plan client indice {index}", Type = DocumentType.Drawing }, $"{part.InternalReference}_ind{index}.pdf", pdf, cancellationToken);
        }

        async Task Photo(PartDetails part, int hue)
        {
            using var png = new MemoryStream(DemoFiles.PhotoPng(hue));
            await documents.AddFileAsync(part.Id, part.CurrentRevisionId!.Value,
                new FileDocumentInput { Title = "Photo de la pièce", Type = DocumentType.Photo }, $"{part.InternalReference}_photo.png", png, cancellationToken);
        }

        static List<OperationInput> Turning(Guid saw, Guid lathe, Guid control, decimal cycle) =>
        [
            new() { Number = 10, Type = OperationType.Cutting, MachineId = saw, Description = "Débit à longueur + 3 mm", SetupTimeMin = 5, CycleTimeMin = 2 },
            new() { Number = 20, Type = OperationType.Turning, MachineId = lathe, Description = "Tournage complet 1re face, ébauche et finition", SetupTimeMin = 45, CycleTimeMin = cycle, NcProgramReference = "O1023" },
            new() { Number = 30, Type = OperationType.Turning, MachineId = lathe, Description = "Reprise 2e face, chanfreins", SetupTimeMin = 20, CycleTimeMin = cycle / 2, NcProgramReference = "O1024" },
            new() { Number = 40, Type = OperationType.Inspection, MachineId = control, Description = "Contrôle des diamètres au micromètre, 1 pièce sur 10", CycleTimeMin = 1.5m },
        ];

        // 1. Arbre avec historique d'indices B → C (scénario de la spécification).
        var shaft = await Part("Arbre de transmission Ø40", dupont, "DE-4521.07", shafts, "Acier 42CrMo4", "B", true, BlankShape.Round, "Ø50 × 420", [safety, series]);
        var shaftSteps = new[] { (60.0, 30.0), (120, 40), (160, 45), (60, 35) };
        await Specs(shaft, r =>
        {
            r.LengthMm = 400; r.DiameterMm = 45; r.MassKg = 4.8m; r.GeneralTolerance = "ISO 2768-mK"; r.SurfaceRoughness = "Ra 1,6 sur portées";
            r.HeatTreatment = "Trempe + revenu"; r.Hardness = "28-32 HRC"; r.SurfaceTreatment = "Brunissage";
            r.WorkshopNotes = "Portées de roulement Ø40 k6 : rectifier après traitement.\nProtéger les filetages pendant le transport.";
            r.InspectionNotes = "Contrôler le battement < 0,02 sur les portées.";
            r.Characteristics = [new() { Name = "Filetage", Value = "M30×1,5" }, new() { Name = "Clavette", Value = "12×8×50" }, new() { Name = "Battement max.", Value = "0,02", Unit = "mm" }];
            r.Operations = [.. Turning(saw, mazak, control, 12), new() { Number = 50, Type = OperationType.Grinding, MachineId = grinder, Description = "Rectification portées Ø40 k6", SetupTimeMin = 30, CycleTimeMin = 8 }];
            r.ChangeDescription = "Premier indice reçu.";
        });
        await Drawing(shaft, "B", "Dupont Énergie", shaftSteps);
        await Photo(shaft, 0);
        var shaftB = (await parts.GetAsync(shaft.Id, cancellationToken)).Value;
        var shaftC = (await revisions.CreateAsync(shaft.Id, new NewRevisionInput
        {
            SourceRevisionId = shaftB.CurrentRevisionId, Index = "C", ChangeOrigin = ChangeOrigin.CustomerRequest,
            ChangeDescription = "Portée centrale passée de Ø45 à Ø48 ; ajout d'un chanfrein 1×45°.",
            DocumentIdsToCopy = [.. (await parts.GetRevisionAsync(shaft.Id, shaftB.CurrentRevisionId!.Value, cancellationToken)).Value.Documents.Where(d => d.Type == DocumentType.Photo).Select(d => d.Id)],
        }, cancellationToken)).Value;
        await Specs(shaft, r => { r.DiameterMm = 48; r.MassKg = 5.1m; r.Characteristics.Add(new() { Name = "Chanfrein", Value = "1×45°" }); }, shaftC.Id);
        await Drawing(shaft, "C", "Dupont Énergie", [(60, 30), (120, 40), (160, 48), (60, 35)], shaftC.Id);
        await revisions.PutInForceAsync(shaft.Id, shaftC.Id, null, cancellationToken);

        // 2. Bague en bronze, plan uniquement papier.
        var ring = await Part("Bague de friction", mecaloire, "ML-0098", rings, "Bronze", "A", true, BlankShape.Tube, "Ø70/Ø40 × 55", [series]);
        await Specs(ring, r =>
        {
            r.DiameterMm = 65; r.LengthMm = 50; r.GeneralTolerance = "ISO 2768-fH"; r.SurfaceRoughness = "Ra 0,8 alésage";
            r.WorkshopNotes = "Alésage Ø40 H7 : contrôler au tampon.";
            r.Characteristics = [new() { Name = "Alésage", Value = "Ø40 H7" }, new() { Name = "Rainure de graissage", Value = "hélicoïdale, pas 20" }];
            r.Operations = Turning(saw, ernault, control, 9);
        });
        var ringRevision = ring.CurrentRevisionId!.Value;
        await documents.AddPaperAsync(ring.Id, ringRevision, new PaperDocumentInput { Title = "Plan client ML-0098", Type = DocumentType.Drawing, PaperLocation = "Bureau, armoire 2, classeur MECALOIRE" }, cancellationToken);

        // 3. Bride : brouillon en attente (aucun indice en vigueur).
        var flange = await Part("Bride de raccordement DN80", hydrau, "HS-BR-80", flanges, "Inox 316L", "01", false, BlankShape.Sheet, "Tôle ép. 20, 220 × 220", [], PartStatus.Prototype);
        await Specs(flange, r =>
        {
            r.LengthMm = 200; r.WidthMm = 200; r.HeightMm = 20; r.GeneralTolerance = "ISO 2768-mK";
            r.Characteristics = [new() { Name = "Perçages", Value = "8 × Ø18 sur Ø160" }];
            r.Operations =
            [
                new() { Number = 10, Type = OperationType.Cutting, MachineId = saw, Description = "Débit tôle" },
                new() { Number = 20, Type = OperationType.Milling, MachineId = haas, Description = "Contournage, perçages, lamages", SetupTimeMin = 60, CycleTimeMin = 25, NcProgramReference = "BR80-01" },
                new() { Number = 30, Type = OperationType.Subcontracting, Description = "Passivation", Subcontractor = "Traitements du Forez" },
            ];
        });
        await Drawing(flange, "01", "Hydrau-Systèmes", [(20, 200)]);

        // 4 à 15. Autres pièces courantes.
        var catalog = new (string Designation, Guid Customer, string Ref, Guid Family, string Material, BlankShape Shape, string Blank, Guid[] Tags, PartStatus Status, (double, double)[] Steps)[]
        {
            ("Axe de chape Ø20", agritec, "AG-772-20", axles, "Acier C45", BlankShape.Round, "Ø25 × 95", [series], PartStatus.Active, [(90, 20)]),
            ("Axe de vérin", hydrau, "HS-AX-12.3", axles, "Inox 304L", BlankShape.Round, "Ø35 × 160", [safety], PartStatus.Active, [(20, 30), (110, 25), (20, 30)]),
            ("Entretoise 10 mm", agritec, "AG-110", spacers, "Aluminium 6082", BlankShape.Tube, "Ø30/Ø12 × 2000", [series], PartStatus.Active, [(10, 28)]),
            ("Entretoise 25 mm", agritec, "AG-125", spacers, "Aluminium 6082", BlankShape.Tube, "Ø30/Ø12 × 2000", [series], PartStatus.Active, [(25, 28)]),
            ("Support moteur", mecaloire, "ML-SUP-4", supports, "Acier S235", BlankShape.Block, "120 × 80 × 60", [], PartStatus.Active, [(120, 60)]),
            ("Arbre intermédiaire", agritec, "AG-ARB-301", shafts, "Acier C45", BlankShape.Round, "Ø45 × 310", [], PartStatus.Dormant, [(80, 30), (150, 40), (70, 30)]),
            ("Bague d'usure PTFE", hydrau, "HS-BU-35", rings, "PTFE", BlankShape.Round, "Ø40 × 1000", [customerMaterial], PartStatus.Active, [(15, 35)]),
            ("Noix de serrage", dupont, "DE-1180", supports, "Laiton de décolletage", BlankShape.Hexagonal, "H27 × 3000", [], PartStatus.Active, [(18, 27)]),
            ("Pignon arbré (ancien modèle)", dupont, "DE-0412", shafts, "Acier 35NiCrMo16", BlankShape.Round, "Ø60 × 250", [], PartStatus.Obsolete, [(60, 40), (100, 56), (60, 40)]),
            ("Plaque d'appui", mecaloire, "ML-PL-77", supports, "Aluminium 2017A", BlankShape.Flat, "Plat 100 × 15", [rework], PartStatus.Active, [(15, 100)]),
            ("Galet de roulement", agritec, "AG-GAL-60", rings, "Polyacétal", BlankShape.Round, "Ø65 × 1000", [], PartStatus.Prototype, [(30, 60)]),
            ("Axe d'articulation Ti", hydrau, "HS-TI-8", axles, "Titane TA6V", BlankShape.Round, "Ø12 × 85", [safety, customerMaterial], PartStatus.Active, [(80, 8)]),
        };

        var hue = 0;
        foreach (var item in catalog)
        {
            var part = await Part(item.Designation, item.Customer, item.Ref, item.Family, item.Material, "A", true, item.Shape, item.Blank, item.Tags, item.Status);
            await Specs(part, r =>
            {
                r.GeneralTolerance = "ISO 2768-mK";
                r.DiameterMm = (decimal)item.Steps.Max(s => s.Item2);
                r.LengthMm = (decimal)item.Steps.Sum(s => s.Item1);
                r.Operations = item.Family == supports
                    ? [new() { Number = 10, Type = OperationType.Cutting, MachineId = saw }, new() { Number = 20, Type = OperationType.Milling, MachineId = huron, SetupTimeMin = 30, CycleTimeMin = 18 }, new() { Number = 30, Type = OperationType.Deburring, CycleTimeMin = 3 }]
                    : Turning(saw, item.Status == PartStatus.Prototype ? ernault : mazak, control, 4);
                if (item.Tags.Contains(customerMaterial))
                {
                    r.MaterialNote = "Matière fournie par le client";
                }
            });
            await Drawing(part, "A", item.Customer == dupont ? "Dupont Énergie" : item.Customer == mecaloire ? "Méca Loire Industrie" : item.Customer == hydrau ? "Hydrau-Systèmes" : "Agritec Machines", item.Steps);
            if (hue++ % 3 == 0)
            {
                await Photo(part, hue % 3);
            }
        }

        LogSeeded(logger, number);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Chargement des données de démonstration…")]
    private static partial void LogSeeding(ILogger logger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Données de démonstration chargées : {Count} pièces.")]
    private static partial void LogSeeded(ILogger logger, int count);
}
