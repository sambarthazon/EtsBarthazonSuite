using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Barthazon.Stock.Migrations.Sqlite.Migrations
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    YearSequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LocationScope = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: false),
                    IncludeWorkInProgress = table.Column<bool>(type: "INTEGER", nullable: false),
                    StartedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    ClosedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    ClosedByName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_inventories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stk_inventory_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InventoryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectKind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PartId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Bucket = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Stage = table.Column<int>(type: "INTEGER", nullable: false),
                    LocationId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Lot = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ExpectedQuantity = table.Column<double>(type: "REAL", nullable: false),
                    CountedQuantity = table.Column<double>(type: "REAL", nullable: true),
                    CountedAt = table.Column<long>(type: "INTEGER", nullable: true),
                    CountedByName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsUnexpected = table.Column<bool>(type: "INTEGER", nullable: false)
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
