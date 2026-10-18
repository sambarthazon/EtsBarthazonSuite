using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Barthazon.Annuaire.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class ContactsMultiples : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ann_Contact",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClientId = table.Column<int>(type: "INTEGER", nullable: true),
                    FournisseurId = table.Column<int>(type: "INTEGER", nullable: true),
                    Nom = table.Column<string>(type: "TEXT", maxLength: 120, nullable: true),
                    Telephone = table.Column<string>(type: "TEXT", maxLength: 40, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Ordre = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ann_Contact", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ann_Contact_ann_Client_ClientId",
                        column: x => x.ClientId,
                        principalTable: "ann_Client",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ann_Contact_ann_Fournisseur_FournisseurId",
                        column: x => x.FournisseurId,
                        principalTable: "ann_Fournisseur",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ann_Contact_ClientId",
                table: "ann_Contact",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_ann_Contact_FournisseurId",
                table: "ann_Contact",
                column: "FournisseurId");

            // Reprise des contacts existants (un par client ou fournisseur), avant la suppression des anciennes colonnes.
            migrationBuilder.Sql(
                """
                INSERT INTO ann_Contact (ClientId, Nom, Telephone, Email, Ordre)
                SELECT Id, nullif(trim(Contact), ''), nullif(trim(Telephone), ''), nullif(trim(Email), ''), 0 FROM ann_Client
                WHERE coalesce(trim(Contact), '') <> '' OR coalesce(trim(Telephone), '') <> '' OR coalesce(trim(Email), '') <> '';
                """);
            migrationBuilder.Sql(
                """
                INSERT INTO ann_Contact (FournisseurId, Nom, Telephone, Email, Ordre)
                SELECT Id, nullif(trim(Contact), ''), nullif(trim(Telephone), ''), nullif(trim(Email), ''), 0 FROM ann_Fournisseur
                WHERE coalesce(trim(Contact), '') <> '' OR coalesce(trim(Telephone), '') <> '' OR coalesce(trim(Email), '') <> '';
                """);

            migrationBuilder.DropColumn(
                name: "Contact",
                table: "ann_Fournisseur");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "ann_Fournisseur");

            migrationBuilder.DropColumn(
                name: "Telephone",
                table: "ann_Fournisseur");

            migrationBuilder.DropColumn(
                name: "Contact",
                table: "ann_Client");

            migrationBuilder.DropColumn(
                name: "Email",
                table: "ann_Client");

            migrationBuilder.DropColumn(
                name: "Telephone",
                table: "ann_Client");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ann_Contact");

            migrationBuilder.AddColumn<string>(
                name: "Contact",
                table: "ann_Fournisseur",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "ann_Fournisseur",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Telephone",
                table: "ann_Fournisseur",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Contact",
                table: "ann_Client",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                table: "ann_Client",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Telephone",
                table: "ann_Client",
                type: "TEXT",
                nullable: true);
        }
    }
}
