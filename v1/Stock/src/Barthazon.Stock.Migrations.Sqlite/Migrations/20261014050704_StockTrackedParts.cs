using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Barthazon.Stock.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class StockTrackedParts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stk_tracked_parts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PartId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LastKnownReference = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    LastKnownDesignation = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    MinimumThreshold = table.Column<int>(type: "INTEGER", nullable: true),
                    DefaultLocationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_tracked_parts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stk_blank_sources",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    TrackedPartId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    ItemId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourcePartId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SourceAfterOperation = table.Column<int>(type: "INTEGER", nullable: true),
                    QuantityPerPart = table.Column<double>(type: "REAL", nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_blank_sources", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stk_blank_sources_stk_tracked_parts_TrackedPartId",
                        column: x => x.TrackedPartId,
                        principalTable: "stk_tracked_parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stk_blank_sources_ItemId",
                table: "stk_blank_sources",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_stk_blank_sources_SourcePartId",
                table: "stk_blank_sources",
                column: "SourcePartId");

            migrationBuilder.CreateIndex(
                name: "IX_stk_blank_sources_TrackedPartId",
                table: "stk_blank_sources",
                column: "TrackedPartId");

            migrationBuilder.CreateIndex(
                name: "IX_stk_tracked_parts_PartId",
                table: "stk_tracked_parts",
                column: "PartId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stk_blank_sources");

            migrationBuilder.DropTable(
                name: "stk_tracked_parts");
        }
    }
}
