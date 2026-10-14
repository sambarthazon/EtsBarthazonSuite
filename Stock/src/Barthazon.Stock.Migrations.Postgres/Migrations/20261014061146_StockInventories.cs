using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Barthazon.Stock.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class StockInventories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stk_inventories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Year = table.Column<int>(type: "integer", nullable: false),
                    YearSequence = table.Column<int>(type: "integer", nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    LocationScope = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    IncludeWorkInProgress = table.Column<bool>(type: "boolean", nullable: false),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ClosedByName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_inventories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stk_inventory_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InventoryId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectKind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bucket = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Lot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ExpectedQuantity = table.Column<decimal>(type: "numeric", nullable: false),
                    CountedQuantity = table.Column<decimal>(type: "numeric", nullable: true),
                    CountedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CountedByName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsUnexpected = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_inventory_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stk_inventory_lines_stk_inventories_InventoryId",
                        column: x => x.InventoryId,
                        principalTable: "stk_inventories",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stk_inventories_Number",
                table: "stk_inventories",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stk_inventories_Status",
                table: "stk_inventories",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_stk_inventories_Year_YearSequence",
                table: "stk_inventories",
                columns: new[] { "Year", "YearSequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UX_stk_inventory_lines_key",
                table: "stk_inventory_lines",
                columns: new[] { "InventoryId", "SubjectKind", "ItemId", "PartId", "RevisionId", "Bucket", "BatchId", "Stage", "LocationId", "Lot" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stk_inventory_lines");

            migrationBuilder.DropTable(
                name: "stk_inventories");
        }
    }
}
