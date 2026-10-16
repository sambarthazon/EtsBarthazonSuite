using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Barthazon.Stock.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class Initiale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stk_Mouvement",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateSaisie = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UtilisateurId = table.Column<int>(type: "INTEGER", nullable: false),
                    TypeReference = table.Column<int>(type: "INTEGER", nullable: false),
                    ReferenceId = table.Column<int>(type: "INTEGER", nullable: false),
                    TypeMouvement = table.Column<int>(type: "INTEGER", nullable: false),
                    Motif = table.Column<int>(type: "INTEGER", nullable: true),
                    Quantite = table.Column<decimal>(type: "TEXT", nullable: false),
                    QuantiteApres = table.Column<decimal>(type: "TEXT", nullable: false),
                    FournisseurId = table.Column<int>(type: "INTEGER", nullable: true),
                    ClientId = table.Column<int>(type: "INTEGER", nullable: true),
                    NumeroBL = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    Commentaire = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AnnuleParId = table.Column<int>(type: "INTEGER", nullable: true),
                    AnnuleId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_Mouvement", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stk_NiveauStock",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TypeReference = table.Column<int>(type: "INTEGER", nullable: false),
                    ReferenceId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantite = table.Column<decimal>(type: "TEXT", nullable: false),
                    SeuilAlerte = table.Column<decimal>(type: "TEXT", nullable: true),
                    DernierMouvement = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_NiveauStock", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stk_Mouvement_Date",
                table: "stk_Mouvement",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_stk_Mouvement_OperationId",
                table: "stk_Mouvement",
                column: "OperationId");

            migrationBuilder.CreateIndex(
                name: "IX_stk_Mouvement_TypeReference_ReferenceId",
                table: "stk_Mouvement",
                columns: new[] { "TypeReference", "ReferenceId" });

            migrationBuilder.CreateIndex(
                name: "IX_stk_NiveauStock_TypeReference_ReferenceId",
                table: "stk_NiveauStock",
                columns: new[] { "TypeReference", "ReferenceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stk_Mouvement");

            migrationBuilder.DropTable(
                name: "stk_NiveauStock");
        }
    }
}
