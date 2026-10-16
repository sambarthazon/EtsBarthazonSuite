using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Barthazon.Annuaire.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class Initiale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ann_Client",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nom = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Contact = table.Column<string>(type: "TEXT", nullable: true),
                    Telephone = table.Column<string>(type: "TEXT", nullable: true),
                    Email = table.Column<string>(type: "TEXT", nullable: true),
                    Remarques = table.Column<string>(type: "TEXT", nullable: true),
                    Archive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ann_Client", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ann_Fournisseur",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nom = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Contact = table.Column<string>(type: "TEXT", nullable: true),
                    Telephone = table.Column<string>(type: "TEXT", nullable: true),
                    Email = table.Column<string>(type: "TEXT", nullable: true),
                    Remarques = table.Column<string>(type: "TEXT", nullable: true),
                    Archive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ann_Fournisseur", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ann_Lien",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PieceFinieId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    PieceCibleId = table.Column<int>(type: "INTEGER", nullable: true),
                    Url = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Libelle = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ann_Lien", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ann_Matiere",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Libelle = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ann_Matiere", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ann_Poste",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Libelle = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Ordre = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ann_Poste", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ann_PieceFinie",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Designation = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ClientId = table.Column<int>(type: "INTEGER", nullable: false),
                    IndiceCourantId = table.Column<int>(type: "INTEGER", nullable: true),
                    EmplacementPlanPapier = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Remarques = table.Column<string>(type: "TEXT", nullable: true),
                    Archive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ann_PieceFinie", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ann_PieceFinie_ann_Client_ClientId",
                        column: x => x.ClientId,
                        principalTable: "ann_Client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ann_Brut",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Reference = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Forme = table.Column<int>(type: "INTEGER", nullable: false),
                    Diametre = table.Column<decimal>(type: "TEXT", nullable: true),
                    DiametreInterieur = table.Column<decimal>(type: "TEXT", nullable: true),
                    Largeur = table.Column<decimal>(type: "TEXT", nullable: true),
                    Epaisseur = table.Column<decimal>(type: "TEXT", nullable: true),
                    Cote = table.Column<decimal>(type: "TEXT", nullable: true),
                    SurPlats = table.Column<decimal>(type: "TEXT", nullable: true),
                    Longueur = table.Column<decimal>(type: "TEXT", nullable: true),
                    DescriptionForme = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    MatiereId = table.Column<int>(type: "INTEGER", nullable: false),
                    Unite = table.Column<int>(type: "INTEGER", nullable: false),
                    Designation = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Remarques = table.Column<string>(type: "TEXT", nullable: true),
                    Archive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ann_Brut", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ann_Brut_ann_Matiere_MatiereId",
                        column: x => x.MatiereId,
                        principalTable: "ann_Matiere",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ann_Indice",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PieceFinieId = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AuteurId = table.Column<int>(type: "INTEGER", nullable: true),
                    DescriptionChangement = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ann_Indice", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ann_Indice_ann_PieceFinie_PieceFinieId",
                        column: x => x.PieceFinieId,
                        principalTable: "ann_PieceFinie",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ann_BrutFournisseur",
                columns: table => new
                {
                    BrutId = table.Column<int>(type: "INTEGER", nullable: false),
                    FournisseurId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReferenceFournisseur = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Principal = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ann_BrutFournisseur", x => new { x.BrutId, x.FournisseurId });
                    table.ForeignKey(
                        name: "FK_ann_BrutFournisseur_ann_Brut_BrutId",
                        column: x => x.BrutId,
                        principalTable: "ann_Brut",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ann_BrutFournisseur_ann_Fournisseur_FournisseurId",
                        column: x => x.FournisseurId,
                        principalTable: "ann_Fournisseur",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ann_PieceFinieBrut",
                columns: table => new
                {
                    PieceFinieId = table.Column<int>(type: "INTEGER", nullable: false),
                    BrutId = table.Column<int>(type: "INTEGER", nullable: false),
                    Principal = table.Column<bool>(type: "INTEGER", nullable: false),
                    PiecesParBrut = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ann_PieceFinieBrut", x => new { x.PieceFinieId, x.BrutId });
                    table.ForeignKey(
                        name: "FK_ann_PieceFinieBrut_ann_Brut_BrutId",
                        column: x => x.BrutId,
                        principalTable: "ann_Brut",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ann_PieceFinieBrut_ann_PieceFinie_PieceFinieId",
                        column: x => x.PieceFinieId,
                        principalTable: "ann_PieceFinie",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ann_OperationGamme",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IndiceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Numero = table.Column<int>(type: "INTEGER", nullable: false),
                    PosteId = table.Column<int>(type: "INTEGER", nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false),
                    TempsEstimeMin = table.Column<int>(type: "INTEGER", nullable: true),
                    Remarque = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Ordre = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ann_OperationGamme", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ann_OperationGamme_ann_Indice_IndiceId",
                        column: x => x.IndiceId,
                        principalTable: "ann_Indice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ann_OperationGamme_ann_Poste_PosteId",
                        column: x => x.PosteId,
                        principalTable: "ann_Poste",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ann_Plan",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IndiceId = table.Column<int>(type: "INTEGER", nullable: false),
                    NomFichier = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Chemin = table.Column<string>(type: "TEXT", maxLength: 400, nullable: false),
                    TypeMime = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Taille = table.Column<long>(type: "INTEGER", nullable: false),
                    DateAjout = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AuteurId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ann_Plan", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ann_Plan_ann_Indice_IndiceId",
                        column: x => x.IndiceId,
                        principalTable: "ann_Indice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ann_Specification",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IndiceId = table.Column<int>(type: "INTEGER", nullable: false),
                    TraitementThermique = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    TraitementSurface = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    ToleranceGenerale = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Masse = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Remarques = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ann_Specification", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ann_Specification_ann_Indice_IndiceId",
                        column: x => x.IndiceId,
                        principalTable: "ann_Indice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ann_SpecificationLigne",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IndiceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Libelle = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Valeur = table.Column<string>(type: "TEXT", maxLength: 300, nullable: false),
                    Ordre = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ann_SpecificationLigne", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ann_SpecificationLigne_ann_Indice_IndiceId",
                        column: x => x.IndiceId,
                        principalTable: "ann_Indice",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "ann_Matiere",
                columns: new[] { "Id", "Libelle" },
                values: new object[,]
                {
                    { 1, "S235" },
                    { 2, "S355" },
                    { 3, "C45" },
                    { 4, "42CrMo4" },
                    { 5, "35NCD16" },
                    { 6, "304L" },
                    { 7, "316L" },
                    { 8, "AU4G (2017)" },
                    { 9, "7075" },
                    { 10, "Laiton" },
                    { 11, "Bronze" },
                    { 12, "PEHD" },
                    { 13, "POM" }
                });

            migrationBuilder.InsertData(
                table: "ann_Poste",
                columns: new[] { "Id", "Libelle", "Ordre" },
                values: new object[,]
                {
                    { 1, "Tour", 10 },
                    { 2, "Tour CN", 20 },
                    { 3, "Fraiseuse", 30 },
                    { 4, "Centre d'usinage", 40 },
                    { 5, "Perceuse", 50 },
                    { 6, "Rectifieuse", 60 },
                    { 7, "Soudure", 70 },
                    { 8, "Traitement extérieur", 80 },
                    { 9, "Contrôle", 90 },
                    { 10, "Autre", 100 }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ann_Brut_MatiereId",
                table: "ann_Brut",
                column: "MatiereId");

            migrationBuilder.CreateIndex(
                name: "IX_ann_Brut_Reference",
                table: "ann_Brut",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ann_BrutFournisseur_FournisseurId",
                table: "ann_BrutFournisseur",
                column: "FournisseurId");

            migrationBuilder.CreateIndex(
                name: "IX_ann_Indice_PieceFinieId",
                table: "ann_Indice",
                column: "PieceFinieId");

            migrationBuilder.CreateIndex(
                name: "IX_ann_Lien_PieceCibleId",
                table: "ann_Lien",
                column: "PieceCibleId");

            migrationBuilder.CreateIndex(
                name: "IX_ann_Lien_PieceFinieId",
                table: "ann_Lien",
                column: "PieceFinieId");

            migrationBuilder.CreateIndex(
                name: "IX_ann_Matiere_Libelle",
                table: "ann_Matiere",
                column: "Libelle",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ann_OperationGamme_IndiceId",
                table: "ann_OperationGamme",
                column: "IndiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ann_OperationGamme_PosteId",
                table: "ann_OperationGamme",
                column: "PosteId");

            migrationBuilder.CreateIndex(
                name: "IX_ann_PieceFinie_ClientId_Reference",
                table: "ann_PieceFinie",
                columns: new[] { "ClientId", "Reference" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ann_PieceFinieBrut_BrutId",
                table: "ann_PieceFinieBrut",
                column: "BrutId");

            migrationBuilder.CreateIndex(
                name: "IX_ann_Plan_IndiceId",
                table: "ann_Plan",
                column: "IndiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ann_Specification_IndiceId",
                table: "ann_Specification",
                column: "IndiceId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ann_SpecificationLigne_IndiceId",
                table: "ann_SpecificationLigne",
                column: "IndiceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ann_BrutFournisseur");

            migrationBuilder.DropTable(
                name: "ann_Lien");

            migrationBuilder.DropTable(
                name: "ann_OperationGamme");

            migrationBuilder.DropTable(
                name: "ann_PieceFinieBrut");

            migrationBuilder.DropTable(
                name: "ann_Plan");

            migrationBuilder.DropTable(
                name: "ann_Specification");

            migrationBuilder.DropTable(
                name: "ann_SpecificationLigne");

            migrationBuilder.DropTable(
                name: "ann_Fournisseur");

            migrationBuilder.DropTable(
                name: "ann_Poste");

            migrationBuilder.DropTable(
                name: "ann_Brut");

            migrationBuilder.DropTable(
                name: "ann_Indice");

            migrationBuilder.DropTable(
                name: "ann_Matiere");

            migrationBuilder.DropTable(
                name: "ann_PieceFinie");

            migrationBuilder.DropTable(
                name: "ann_Client");
        }
    }
}
