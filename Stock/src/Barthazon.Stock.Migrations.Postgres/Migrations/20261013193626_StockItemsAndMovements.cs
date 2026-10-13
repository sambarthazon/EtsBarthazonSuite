using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Barthazon.Stock.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class StockItemsAndMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "stk_balances",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectKind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: false),
                    PartId = table.Column<Guid>(type: "uuid", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bucket = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    Stage = table.Column<int>(type: "integer", nullable: false),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Lot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Quantity = table.Column<decimal>(type: "numeric", nullable: false),
                    LastMovementAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_balances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stk_items",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Code = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    Designation = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MaterialId = table.Column<Guid>(type: "uuid", nullable: true),
                    Shape = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    Dimensions = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Unit = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    OwnerCustomerId = table.Column<Guid>(type: "uuid", nullable: true),
                    MinimumThreshold = table.Column<decimal>(type: "numeric", nullable: true),
                    DefaultLocationId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReferenceUnitCost = table.Column<decimal>(type: "numeric", nullable: true),
                    UsualSupplier = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    TrackLots = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    SearchText = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedBy = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_items", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stk_transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Sequence = table.Column<long>(type: "bigint", nullable: false),
                    Type = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RecordedBy = table.Column<Guid>(type: "uuid", nullable: true),
                    RecordedByName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    StepSequence = table.Column<int>(type: "integer", nullable: true),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: true),
                    Comment = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ReversesTransactionId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReversedByTransactionId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_transactions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stk_transaction_lines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    SubjectKind = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    ItemId = table.Column<Guid>(type: "uuid", nullable: true),
                    PartId = table.Column<Guid>(type: "uuid", nullable: true),
                    RevisionId = table.Column<Guid>(type: "uuid", nullable: true),
                    Bucket = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    BatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    Stage = table.Column<int>(type: "integer", nullable: true),
                    LocationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Lot = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    Delta = table.Column<decimal>(type: "numeric", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_transaction_lines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stk_transaction_lines_stk_transactions_TransactionId",
                        column: x => x.TransactionId,
                        principalTable: "stk_transactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stk_balances_BatchId",
                table: "stk_balances",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_stk_balances_ItemId",
                table: "stk_balances",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_stk_balances_LocationId",
                table: "stk_balances",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_stk_balances_PartId",
                table: "stk_balances",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "UX_stk_balances_key",
                table: "stk_balances",
                columns: new[] { "SubjectKind", "ItemId", "PartId", "RevisionId", "Bucket", "BatchId", "Stage", "LocationId", "Lot" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stk_items_Code",
                table: "stk_items",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stk_items_MaterialId",
                table: "stk_items",
                column: "MaterialId");

            migrationBuilder.CreateIndex(
                name: "IX_stk_items_OwnerCustomerId",
                table: "stk_items",
                column: "OwnerCustomerId");

            migrationBuilder.CreateIndex(
                name: "IX_stk_transaction_lines_BatchId",
                table: "stk_transaction_lines",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_stk_transaction_lines_ItemId",
                table: "stk_transaction_lines",
                column: "ItemId");

            migrationBuilder.CreateIndex(
                name: "IX_stk_transaction_lines_LocationId",
                table: "stk_transaction_lines",
                column: "LocationId");

            migrationBuilder.CreateIndex(
                name: "IX_stk_transaction_lines_PartId",
                table: "stk_transaction_lines",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_stk_transaction_lines_TransactionId",
                table: "stk_transaction_lines",
                column: "TransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_stk_transactions_BatchId",
                table: "stk_transactions",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_stk_transactions_OccurredAt",
                table: "stk_transactions",
                column: "OccurredAt");

            migrationBuilder.CreateIndex(
                name: "IX_stk_transactions_Sequence",
                table: "stk_transactions",
                column: "Sequence",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stk_balances");

            migrationBuilder.DropTable(
                name: "stk_items");

            migrationBuilder.DropTable(
                name: "stk_transaction_lines");

            migrationBuilder.DropTable(
                name: "stk_transactions");
        }
    }
}
