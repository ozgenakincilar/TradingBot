using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpotAccountReconciliation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReconciliationRuns",
                schema: "operations",
                columns: table => new
                {
                    Exchange = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    SnapshotId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    SnapshotHash = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    SnapshotOccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CanTrade = table.Column<bool>(type: "bit", nullable: false),
                    IsConsistent = table.Column<bool>(type: "bit", nullable: false),
                    DiscrepancyCount = table.Column<int>(type: "int", nullable: false),
                    DiscrepanciesJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReconciliationRuns", x => new { x.Exchange, x.SnapshotId });
                    table.CheckConstraint("CK_ReconciliationRuns_DiscrepancyCount", "[DiscrepancyCount] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "TradingSafetyStates",
                schema: "operations",
                columns: table => new
                {
                    Exchange = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    IsHalted = table.Column<bool>(type: "bit", nullable: false),
                    HaltReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradingSafetyStates", x => x.Exchange);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReconciliationRuns_Exchange_SnapshotOccurredAt",
                schema: "operations",
                table: "ReconciliationRuns",
                columns: new[] { "Exchange", "SnapshotOccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReconciliationRuns",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "TradingSafetyStates",
                schema: "operations");
        }
    }
}
