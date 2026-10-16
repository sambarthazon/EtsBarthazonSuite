using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Barthazon.Commun.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class Initiale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "com_Historique",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UtilisateurId = table.Column<int>(type: "INTEGER", nullable: true),
                    Module = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    TypeObjet = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ObjetId = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    Phrase = table.Column<string>(type: "TEXT", maxLength: 500, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_com_Historique", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "com_Parametre",
                columns: table => new
                {
                    Cle = table.Column<string>(type: "TEXT", maxLength: 80, nullable: false),
                    Valeur = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_com_Parametre", x => x.Cle);
                });

            migrationBuilder.CreateTable(
                name: "com_Utilisateur",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Prenom = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Nom = table.Column<string>(type: "TEXT", maxLength: 60, nullable: false),
                    Initiales = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    PinHash = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<int>(type: "INTEGER", nullable: false),
                    Actif = table.Column<bool>(type: "INTEGER", nullable: false),
                    DerniereApplication = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    EchecsPin = table.Column<int>(type: "INTEGER", nullable: false),
                    BloqueJusqua = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_com_Utilisateur", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_com_Historique_Date",
                table: "com_Historique",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_com_Historique_TypeObjet_ObjetId",
                table: "com_Historique",
                columns: new[] { "TypeObjet", "ObjetId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "com_Historique");

            migrationBuilder.DropTable(
                name: "com_Parametre");

            migrationBuilder.DropTable(
                name: "com_Utilisateur");
        }
    }
}
