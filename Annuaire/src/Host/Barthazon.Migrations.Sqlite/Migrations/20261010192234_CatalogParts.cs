using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Barthazon.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class CatalogParts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CurrentRevisionId",
                table: "cat_parts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "cat_parts",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "CustomerReference",
                table: "cat_parts",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "cat_parts",
                type: "TEXT",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Designation",
                table: "cat_parts",
                type: "TEXT",
                maxLength: 200,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "FamilyId",
                table: "cat_parts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InternalReference",
                table: "cat_parts",
                type: "TEXT",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "MainPhotoDocumentId",
                table: "cat_parts",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "RevisionsChangedAt",
                table: "cat_parts",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SearchText",
                table: "cat_parts",
                type: "TEXT",
                maxLength: 4000,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "cat_parts",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "cat_favorites",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PartId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cat_favorites", x => new { x.UserId, x.PartId });
                    table.ForeignKey(
                        name: "FK_cat_favorites_cat_parts_PartId",
                        column: x => x.PartId,
                        principalTable: "cat_parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cat_part_links",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SourcePartId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TargetPartId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Quantity = table.Column<double>(type: "REAL", precision: 12, scale: 3, nullable: true),
                    Note = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cat_part_links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cat_part_links_cat_parts_SourcePartId",
                        column: x => x.SourcePartId,
                        principalTable: "cat_parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_cat_part_links_cat_parts_TargetPartId",
                        column: x => x.TargetPartId,
                        principalTable: "cat_parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cat_part_revisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PartId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Index = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ChangeDescription = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ChangeOrigin = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    MaterialId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MaterialNote = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    BlankShape = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    BlankDimensions = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LengthMm = table.Column<double>(type: "REAL", precision: 12, scale: 3, nullable: true),
                    WidthMm = table.Column<double>(type: "REAL", precision: 12, scale: 3, nullable: true),
                    HeightMm = table.Column<double>(type: "REAL", precision: 12, scale: 3, nullable: true),
                    DiameterMm = table.Column<double>(type: "REAL", precision: 12, scale: 3, nullable: true),
                    MassKg = table.Column<double>(type: "REAL", precision: 12, scale: 3, nullable: true),
                    GeneralTolerance = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SurfaceRoughness = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    HeatTreatment = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SurfaceTreatment = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Hardness = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Tooling = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    WorkshopNotes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    InspectionNotes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cat_part_revisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cat_part_revisions_cat_parts_PartId",
                        column: x => x.PartId,
                        principalTable: "cat_parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cat_part_tags",
                columns: table => new
                {
                    PartId = table.Column<Guid>(type: "TEXT", nullable: false),
                    TagId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cat_part_tags", x => new { x.PartId, x.TagId });
                    table.ForeignKey(
                        name: "FK_cat_part_tags_cat_parts_PartId",
                        column: x => x.PartId,
                        principalTable: "cat_parts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_cat_part_tags_cat_tags_TagId",
                        column: x => x.TagId,
                        principalTable: "cat_tags",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "cat_documents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PartId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 260, nullable: true),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    SizeBytes = table.Column<long>(type: "INTEGER", nullable: true),
                    Sha256 = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    StorageKey = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    PaperLocation = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    Order = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cat_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cat_documents_cat_part_revisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "cat_part_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cat_part_characteristics",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PartId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Unit = table.Column<string>(type: "TEXT", maxLength: 30, nullable: true),
                    Order = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cat_part_characteristics", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cat_part_characteristics_cat_part_revisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "cat_part_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cat_routing_operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PartId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    MachineId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    SetupTimeMin = table.Column<double>(type: "REAL", precision: 10, scale: 2, nullable: true),
                    CycleTimeMin = table.Column<double>(type: "REAL", precision: 10, scale: 2, nullable: true),
                    NcProgramReference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Tooling = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Subcontractor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cat_routing_operations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cat_routing_operations_cat_part_revisions_RevisionId",
                        column: x => x.RevisionId,
                        principalTable: "cat_part_revisions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cat_parts_CustomerId_CustomerReference",
                table: "cat_parts",
                columns: new[] { "CustomerId", "CustomerReference" });

            migrationBuilder.CreateIndex(
                name: "IX_cat_parts_FamilyId",
                table: "cat_parts",
                column: "FamilyId");

            migrationBuilder.CreateIndex(
                name: "IX_cat_parts_InternalReference",
                table: "cat_parts",
                column: "InternalReference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cat_documents_PartId",
                table: "cat_documents",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_cat_documents_RevisionId",
                table: "cat_documents",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_cat_documents_StorageKey",
                table: "cat_documents",
                column: "StorageKey");

            migrationBuilder.CreateIndex(
                name: "IX_cat_favorites_PartId",
                table: "cat_favorites",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_cat_part_characteristics_RevisionId",
                table: "cat_part_characteristics",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_cat_part_links_SourcePartId_TargetPartId_Type",
                table: "cat_part_links",
                columns: new[] { "SourcePartId", "TargetPartId", "Type" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cat_part_links_TargetPartId",
                table: "cat_part_links",
                column: "TargetPartId");

            migrationBuilder.CreateIndex(
                name: "IX_cat_part_revisions_MaterialId",
                table: "cat_part_revisions",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_cat_part_revisions_PartId_Index",
                table: "cat_part_revisions",
                columns: new[] { "PartId", "Index" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cat_part_tags_TagId",
                table: "cat_part_tags",
                column: "TagId");

            migrationBuilder.CreateIndex(
                name: "IX_cat_routing_operations_MachineId",
                table: "cat_routing_operations",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_cat_routing_operations_RevisionId",
                table: "cat_routing_operations",
                column: "RevisionId");

            migrationBuilder.AddForeignKey(
                name: "FK_cat_parts_cat_part_families_FamilyId",
                table: "cat_parts",
                column: "FamilyId",
                principalTable: "cat_part_families",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_cat_parts_cat_part_families_FamilyId",
                table: "cat_parts");

            migrationBuilder.DropTable(
                name: "cat_documents");

            migrationBuilder.DropTable(
                name: "cat_favorites");

            migrationBuilder.DropTable(
                name: "cat_part_characteristics");

            migrationBuilder.DropTable(
                name: "cat_part_links");

            migrationBuilder.DropTable(
                name: "cat_part_tags");

            migrationBuilder.DropTable(
                name: "cat_routing_operations");

            migrationBuilder.DropTable(
                name: "cat_part_revisions");

            migrationBuilder.DropIndex(
                name: "IX_cat_parts_CustomerId_CustomerReference",
                table: "cat_parts");

            migrationBuilder.DropIndex(
                name: "IX_cat_parts_FamilyId",
                table: "cat_parts");

            migrationBuilder.DropIndex(
                name: "IX_cat_parts_InternalReference",
                table: "cat_parts");

            migrationBuilder.DropColumn(
                name: "CurrentRevisionId",
                table: "cat_parts");

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "cat_parts");

            migrationBuilder.DropColumn(
                name: "CustomerReference",
                table: "cat_parts");

            migrationBuilder.DropColumn(
                name: "Description",
                table: "cat_parts");

            migrationBuilder.DropColumn(
                name: "Designation",
                table: "cat_parts");

            migrationBuilder.DropColumn(
                name: "FamilyId",
                table: "cat_parts");

            migrationBuilder.DropColumn(
                name: "InternalReference",
                table: "cat_parts");

            migrationBuilder.DropColumn(
                name: "MainPhotoDocumentId",
                table: "cat_parts");

            migrationBuilder.DropColumn(
                name: "RevisionsChangedAt",
                table: "cat_parts");

            migrationBuilder.DropColumn(
                name: "SearchText",
                table: "cat_parts");

            migrationBuilder.DropColumn(
                name: "Status",
                table: "cat_parts");
        }
    }
}
