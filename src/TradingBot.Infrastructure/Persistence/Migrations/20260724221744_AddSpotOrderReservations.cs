using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSpotOrderReservations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OrderId",
                schema: "portfolio",
                table: "SpotExecutions",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SpotOrderReservations",
                schema: "portfolio",
                columns: table => new
                {
                    OrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Exchange = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    Symbol = table.Column<string>(type: "varchar(32)", unicode: false, maxLength: 32, nullable: false),
                    BaseAsset = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    QuoteAsset = table.Column<string>(type: "varchar(12)", unicode: false, maxLength: 12, nullable: false),
                    Side = table.Column<byte>(type: "tinyint", nullable: false),
                    ApprovedQuantity = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    FilledQuantity = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    RemainingReserved = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    Status = table.Column<byte>(type: "tinyint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpotOrderReservations", x => x.OrderId);
                    table.CheckConstraint("CK_SpotOrderReservations_ApprovedQuantity", "[ApprovedQuantity] > 0");
                    table.CheckConstraint("CK_SpotOrderReservations_FilledQuantity", "[FilledQuantity] >= 0 AND [FilledQuantity] <= [ApprovedQuantity]");
                    table.CheckConstraint("CK_SpotOrderReservations_RemainingReserved", "[RemainingReserved] >= 0");
                    table.ForeignKey(
                        name: "FK_SpotOrderReservations_Orders_OrderId",
                        column: x => x.OrderId,
                        principalSchema: "execution",
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SpotExecutions_OrderId",
                schema: "portfolio",
                table: "SpotExecutions",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_SpotOrderReservations_Exchange_Symbol_Status",
                schema: "portfolio",
                table: "SpotOrderReservations",
                columns: new[] { "Exchange", "Symbol", "Status" });

            migrationBuilder.AddForeignKey(
                name: "FK_SpotExecutions_Orders_OrderId",
                schema: "portfolio",
                table: "SpotExecutions",
                column: "OrderId",
                principalSchema: "execution",
                principalTable: "Orders",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SpotExecutions_Orders_OrderId",
                schema: "portfolio",
                table: "SpotExecutions");

            migrationBuilder.DropTable(
                name: "SpotOrderReservations",
                schema: "portfolio");

            migrationBuilder.DropIndex(
                name: "IX_SpotExecutions_OrderId",
                schema: "portfolio",
                table: "SpotExecutions");

            migrationBuilder.DropColumn(
                name: "OrderId",
                schema: "portfolio",
                table: "SpotExecutions");
        }
    }
}
