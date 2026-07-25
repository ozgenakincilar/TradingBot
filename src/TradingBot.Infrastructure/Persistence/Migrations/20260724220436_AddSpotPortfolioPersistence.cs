using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpotPortfolioPersistence : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "portfolio");

            migrationBuilder.CreateTable(
                name: "AssetBalances",
                schema: "portfolio",
                columns: table => new
                {
                    Exchange = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Asset = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    Reserved = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssetBalances", x => new { x.Exchange, x.Asset });
                    table.CheckConstraint("CK_AssetBalances_Reserved", "[Reserved] >= 0 AND [Reserved] <= [Total]");
                    table.CheckConstraint("CK_AssetBalances_Total", "[Total] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "SpotExecutions",
                schema: "portfolio",
                columns: table => new
                {
                    Exchange = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    ExchangeExecutionId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    Symbol = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Side = table.Column<byte>(type: "tinyint", nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    QuoteFee = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    RealizedPnl = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    CorrelationId = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpotExecutions", x => new { x.Exchange, x.ExchangeExecutionId });
                    table.CheckConstraint("CK_SpotExecutions_Price", "[Price] > 0");
                    table.CheckConstraint("CK_SpotExecutions_Quantity", "[Quantity] > 0");
                    table.CheckConstraint("CK_SpotExecutions_QuoteFee", "[QuoteFee] >= 0");
                });

            migrationBuilder.CreateTable(
                name: "SpotPositions",
                schema: "portfolio",
                columns: table => new
                {
                    Exchange = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Symbol = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    BaseAsset = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    QuoteAsset = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    OpenQuantity = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    ReservedSellQuantity = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    AverageEntryPrice = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    RealizedPnl = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpotPositions", x => new { x.Exchange, x.Symbol });
                    table.CheckConstraint("CK_SpotPositions_AverageEntryPrice", "([OpenQuantity] = 0 AND [AverageEntryPrice] = 0) OR ([OpenQuantity] > 0 AND [AverageEntryPrice] > 0)");
                    table.CheckConstraint("CK_SpotPositions_OpenQuantity", "[OpenQuantity] >= 0");
                    table.CheckConstraint("CK_SpotPositions_ReservedSellQuantity", "[ReservedSellQuantity] >= 0 AND [ReservedSellQuantity] <= [OpenQuantity]");
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpotExecutions_Exchange_Symbol_OccurredAt",
                schema: "portfolio",
                table: "SpotExecutions",
                columns: new[] { "Exchange", "Symbol", "OccurredAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AssetBalances",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "SpotExecutions",
                schema: "portfolio");

            migrationBuilder.DropTable(
                name: "SpotPositions",
                schema: "portfolio");
        }
    }
}
