using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Barthazon.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cat_part_families",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cat_part_families", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cat_tags",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Label = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cat_tags", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ref_customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Address = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    PostalCode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    City = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ref_customers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ref_machines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ref_machines", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ref_materials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Designation = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Grade = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Family = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    DensityKgDm3 = table.Column<decimal>(type: "numeric(8,3)", precision: 8, scale: 3, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ref_materials", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ref_users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    PasswordHash = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    LastLoginAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ref_users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sys_audit_entries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UserName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    EntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    EntityId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ParentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Details = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sys_audit_entries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "sys_settings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sys_settings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "cat_parts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InternalReference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Designation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    FamilyId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    MainPhotoDocumentId = table.Column<Guid>(type: "uuid", nullable: true),
                    CurrentRevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    SearchText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    RevisionsChangedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cat_parts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_cat_parts_cat_part_families_FamilyId",
                        column: x => x.FamilyId,
                        principalTable: "cat_part_families",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ref_customer_contacts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CustomerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Function = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Phone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Email = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ref_customer_contacts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ref_customer_contacts_ref_customers_CustomerId",
                        column: x => x.CustomerId,
                        principalTable: "ref_customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "cat_favorites",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SourcePartId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetPartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    Note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    Index = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Sequence = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: true),
                    ChangeDescription = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    ChangeOrigin = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    MaterialId = table.Column<Guid>(type: "uuid", nullable: true),
                    MaterialNote = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    BlankShape = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    BlankDimensions = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    LengthMm = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    WidthMm = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    HeightMm = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    DiameterMm = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    MassKg = table.Column<decimal>(type: "numeric(12,3)", precision: 12, scale: 3, nullable: true),
                    GeneralTolerance = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SurfaceRoughness = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    HeatTreatment = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    SurfaceTreatment = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Hardness = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Tooling = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    WorkshopNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    InspectionNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    TagId = table.Column<Guid>(type: "uuid", nullable: false)
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Title = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: true),
                    ContentType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: true),
                    Sha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    StorageKey = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    PaperLocation = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Value = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Unit = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Order = table.Column<int>(type: "integer", nullable: false)
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
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Number = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    MachineId = table.Column<Guid>(type: "uuid", nullable: true),
                    Description = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SetupTimeMin = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    CycleTimeMin = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    NcProgramReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Tooling = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    Subcontractor = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true)
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
                name: "IX_cat_part_families_Name",
                table: "cat_part_families",
                column: "Name",
                unique: true);

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
                name: "IX_cat_routing_operations_MachineId",
                table: "cat_routing_operations",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_cat_routing_operations_RevisionId",
                table: "cat_routing_operations",
                column: "RevisionId");

            migrationBuilder.CreateIndex(
                name: "IX_cat_tags_Label",
                table: "cat_tags",
                column: "Label",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ref_customer_contacts_CustomerId",
                table: "ref_customer_contacts",
                column: "CustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_ref_customers_Code",
                table: "ref_customers",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ref_machines_Name",
                table: "ref_machines",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ref_materials_Designation_Grade",
                table: "ref_materials",
                columns: new[] { "Designation", "Grade" });

            migrationBuilder.CreateIndex(
                name: "IX_ref_users_UserName",
                table: "ref_users",
                column: "UserName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_sys_audit_entries_At",
                table: "sys_audit_entries",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_sys_audit_entries_EntityType_EntityId",
                table: "sys_audit_entries",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_sys_audit_entries_ParentId",
                table: "sys_audit_entries",
                column: "ParentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
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
                name: "ref_customer_contacts");

            migrationBuilder.DropTable(
                name: "ref_machines");

            migrationBuilder.DropTable(
                name: "ref_materials");

            migrationBuilder.DropTable(
                name: "ref_users");

            migrationBuilder.DropTable(
                name: "sys_audit_entries");

            migrationBuilder.DropTable(
                name: "sys_settings");

            migrationBuilder.DropTable(
                name: "cat_tags");

            migrationBuilder.DropTable(
                name: "cat_part_revisions");

            migrationBuilder.DropTable(
                name: "ref_customers");

            migrationBuilder.DropTable(
                name: "cat_parts");

            migrationBuilder.DropTable(
                name: "cat_part_families");
        }
    }
}
