using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Barthazon.Annuaire.Donnees.Migrations
{
    /// <inheritdoc />
    public partial class SpecificationsLibres : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Unite",
                table: "ann_SpecificationLigne",
                type: "TEXT",
                maxLength: 20,
                nullable: true);

            // Reprise : la masse, les traitements et la tolérance deviennent des lignes, placées avant les lignes existantes.
            foreach (var (colonne, libelle, ordre) in new[]
            {
                ("Masse", "Masse", -4),
                ("TraitementThermique", "Traitement thermique", -3),
                ("TraitementSurface", "Traitement de surface", -2),
                ("ToleranceGenerale", "Tolérance générale", -1),
            })
            {
                migrationBuilder.Sql(
                    $"INSERT INTO ann_SpecificationLigne (IndiceId, Libelle, Valeur, Ordre) " +
                    $"SELECT IndiceId, '{libelle.Replace("'", "''", System.StringComparison.Ordinal)}', {colonne}, {ordre} FROM ann_Specification " +
                    $"WHERE {colonne} IS NOT NULL AND trim({colonne}) <> '';");
            }

            migrationBuilder.DropColumn(
                name: "Masse",
                table: "ann_Specification");

            migrationBuilder.DropColumn(
                name: "ToleranceGenerale",
                table: "ann_Specification");

            migrationBuilder.DropColumn(
                name: "TraitementSurface",
                table: "ann_Specification");

            migrationBuilder.DropColumn(
                name: "TraitementThermique",
                table: "ann_Specification");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Unite",
                table: "ann_SpecificationLigne");

            migrationBuilder.AddColumn<string>(
                name: "Masse",
                table: "ann_Specification",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ToleranceGenerale",
                table: "ann_Specification",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraitementSurface",
                table: "ann_Specification",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TraitementThermique",
                table: "ann_Specification",
                type: "TEXT",
                maxLength: 200,
                nullable: true);
        }
    }
}
