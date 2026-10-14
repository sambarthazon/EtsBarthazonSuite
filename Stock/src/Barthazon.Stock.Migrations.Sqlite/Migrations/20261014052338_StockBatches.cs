using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Barthazon.Stock.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class StockBatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "FirstMovementAt",
                table: "stk_balances",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.CreateTable(
                name: "stk_batches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Number = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Year = table.Column<int>(type: "INTEGER", nullable: false),
                    YearSequence = table.Column<int>(type: "INTEGER", nullable: false),
                    PartId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RevisionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PartReference = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    RevisionIndex = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    LaunchedQuantity = table.Column<int>(type: "INTEGER", nullable: false),
                    LaunchedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CustomerOrderReference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsRecovery = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    ClosedOn = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    UpdatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    Version = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_batches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "stk_batch_steps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    BatchId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<int>(type: "INTEGER", nullable: false),
                    OperationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    OperationNumber = table.Column<int>(type: "INTEGER", nullable: false),
                    OperationType = table.Column<string>(type: "TEXT", maxLength: 30, nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    MachineId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Subcontractor = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    SetupTimeMin = table.Column<double>(type: "REAL", nullable: true),
                    CycleTimeMin = table.Column<double>(type: "REAL", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stk_batch_steps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_stk_batch_steps_stk_batches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "stk_batches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_stk_batch_steps_BatchId",
                table: "stk_batch_steps",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_stk_batch_steps_MachineId",
                table: "stk_batch_steps",
                column: "MachineId");

            migrationBuilder.CreateIndex(
                name: "IX_stk_batches_Number",
                table: "stk_batches",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_stk_batches_PartId",
                table: "stk_batches",
                column: "PartId");

            migrationBuilder.CreateIndex(
                name: "IX_stk_batches_Status",
                table: "stk_batches",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_stk_batches_Year_YearSequence",
                table: "stk_batches",
                columns: new[] { "Year", "YearSequence" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "stk_batch_steps");

            migrationBuilder.DropTable(
                name: "stk_batches");

            migrationBuilder.DropColumn(
                name: "FirstMovementAt",
                table: "stk_balances");
        }
    }
}
