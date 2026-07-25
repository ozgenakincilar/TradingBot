using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "operations");

            migrationBuilder.EnsureSchema(
                name: "execution");

            migrationBuilder.EnsureSchema(
                name: "risk");

            migrationBuilder.CreateTable(
                name: "AuditEvents",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    Category = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Action = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    AggregateType = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    AggregateId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    CorrelationId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    Data = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditEvents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Orders",
                schema: "execution",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ClientOrderId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    Exchange = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Symbol = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Side = table.Column<byte>(type: "tinyint", nullable: false),
                    Type = table.Column<byte>(type: "tinyint", nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    RequestedQuantity = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    ApprovedQuantity = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    LimitPrice = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: true),
                    FilledQuantity = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    AverageFillPrice = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: true),
                    ExchangeOrderId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Orders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "operations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    MessageType = table.Column<string>(type: "varchar(256)", unicode: false, maxLength: 256, nullable: false),
                    Payload = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CorrelationId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: true),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    AttemptCount = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(2048)", maxLength: 2048, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RiskDecisions",
                schema: "risk",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DecisionType = table.Column<byte>(type: "tinyint", nullable: false),
                    ApprovedQuantity = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: true),
                    RejectionCode = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(512)", maxLength: 512, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RiskDecisions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditEvents_AggregateType_AggregateId_OccurredAt",
                schema: "operations",
                table: "AuditEvents",
                columns: new[] { "AggregateType", "AggregateId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_Orders_ClientOrderId",
                schema: "execution",
                table: "Orders",
                column: "ClientOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Orders_Exchange_Symbol_Status",
                schema: "execution",
                table: "Orders",
                columns: new[] { "Exchange", "Symbol", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_ProcessedAt_OccurredAt",
                schema: "operations",
                table: "OutboxMessages",
                columns: new[] { "ProcessedAt", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RiskDecisions_OrderId_OccurredAt",
                schema: "risk",
                table: "RiskDecisions",
                columns: new[] { "OrderId", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AuditEvents",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "Orders",
                schema: "execution");

            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "RiskDecisions",
                schema: "risk");
        }
    }
}
