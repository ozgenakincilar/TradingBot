using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWalkForwardResults : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "research");

            migrationBuilder.CreateTable(
                name: "WalkForwardRuns",
                schema: "research",
                columns: table => new
                {
                    RunSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ScheduleSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ReportSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    SchemaVersion = table.Column<string>(type: "varchar(64)", unicode: false, maxLength: 64, nullable: false),
                    StrategyId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    StrategyVersion = table.Column<int>(type: "int", nullable: false),
                    TrainingMode = table.Column<int>(type: "int", nullable: false),
                    TrainingDurationTicks = table.Column<long>(type: "bigint", nullable: false),
                    ValidationDurationTicks = table.Column<long>(type: "bigint", nullable: false),
                    OutOfSampleDurationTicks = table.Column<long>(type: "bigint", nullable: false),
                    WindowCount = table.Column<int>(type: "int", nullable: false),
                    ProfitableWindowCount = table.Column<int>(type: "int", nullable: false),
                    TotalCompletedTradeCount = table.Column<int>(type: "int", nullable: false),
                    TotalFees = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    MeanNetReturnPercent = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    MedianNetReturnPercent = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    WorstNetReturnPercent = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    BestNetReturnPercent = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    CompoundedNetReturnPercent = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    MeanMaximumDrawdownPercent = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalkForwardRuns", x => x.RunSha256);
                    table.CheckConstraint("CK_WalkForwardRuns_Durations", "[TrainingDurationTicks] > 0 AND [ValidationDurationTicks] > 0 AND [OutOfSampleDurationTicks] > 0");
                    table.CheckConstraint("CK_WalkForwardRuns_MeanDrawdown", "[MeanMaximumDrawdownPercent] >= 0 AND [MeanMaximumDrawdownPercent] <= 100");
                    table.CheckConstraint("CK_WalkForwardRuns_StrategyVersion", "[StrategyVersion] > 0");
                    table.CheckConstraint("CK_WalkForwardRuns_TotalFees", "[TotalFees] >= 0");
                    table.CheckConstraint("CK_WalkForwardRuns_TradeCount", "[TotalCompletedTradeCount] >= 0");
                    table.CheckConstraint("CK_WalkForwardRuns_TrainingMode", "[TrainingMode] IN (1, 2)");
                    table.CheckConstraint("CK_WalkForwardRuns_WindowCounts", "[WindowCount] > 0 AND [ProfitableWindowCount] >= 0 AND [ProfitableWindowCount] <= [WindowCount]");
                });

            migrationBuilder.CreateTable(
                name: "WalkForwardWindowResults",
                schema: "research",
                columns: table => new
                {
                    RunSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    WindowIndex = table.Column<int>(type: "int", nullable: false),
                    ManifestSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    TrainStartInclusive = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    TrainEndExclusive = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    ValidationEndExclusive = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    OutOfSampleEndExclusive = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    InitialQuoteBalance = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    EndingCashBalance = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    OpenQuantity = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    NetLiquidationValue = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    GrossReturnPercent = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    NetReturnPercent = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    RealizedPnl = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    GrossProfit = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    GrossLoss = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    Expectancy = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: true),
                    TotalFees = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    EstimatedSpreadCost = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    EstimatedSlippageCost = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    MaximumDrawdownPercent = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: false),
                    FillCount = table.Column<int>(type: "int", nullable: false),
                    CompletedTradeCount = table.Column<int>(type: "int", nullable: false),
                    WinningTradeCount = table.Column<int>(type: "int", nullable: false),
                    WinRatePercent = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: true),
                    ProfitFactor = table.Column<decimal>(type: "decimal(38,18)", precision: 38, scale: 18, nullable: true),
                    AverageHoldingTimeTicks = table.Column<long>(type: "bigint", nullable: true),
                    HasPendingExecution = table.Column<bool>(type: "bit", nullable: false),
                    FirstFillAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true),
                    LastFillAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WalkForwardWindowResults", x => new { x.RunSha256, x.WindowIndex });
                    table.CheckConstraint("CK_WalkForwardWindowResults_Balances", "[InitialQuoteBalance] > 0 AND [EndingCashBalance] >= 0 AND [OpenQuantity] >= 0 AND [NetLiquidationValue] >= 0");
                    table.CheckConstraint("CK_WalkForwardWindowResults_Costs", "[GrossProfit] >= 0 AND [GrossLoss] >= 0 AND [TotalFees] >= 0 AND [EstimatedSpreadCost] >= 0 AND [EstimatedSlippageCost] >= 0");
                    table.CheckConstraint("CK_WalkForwardWindowResults_Counts", "[FillCount] >= 0 AND [CompletedTradeCount] >= 0 AND [WinningTradeCount] >= 0 AND [WinningTradeCount] <= [CompletedTradeCount]");
                    table.CheckConstraint("CK_WalkForwardWindowResults_Drawdown", "[MaximumDrawdownPercent] >= 0 AND [MaximumDrawdownPercent] <= 100");
                    table.CheckConstraint("CK_WalkForwardWindowResults_Index", "[WindowIndex] >= 0");
                    table.CheckConstraint("CK_WalkForwardWindowResults_Times", "[TrainStartInclusive] < [TrainEndExclusive] AND [TrainEndExclusive] < [ValidationEndExclusive] AND [ValidationEndExclusive] < [OutOfSampleEndExclusive]");
                    table.ForeignKey(
                        name: "FK_WalkForwardWindowResults_WalkForwardRuns_RunSha256",
                        column: x => x.RunSha256,
                        principalSchema: "research",
                        principalTable: "WalkForwardRuns",
                        principalColumn: "RunSha256",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WalkForwardRuns_ReportSha256",
                schema: "research",
                table: "WalkForwardRuns",
                column: "ReportSha256",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_WalkForwardRuns_ScheduleSha256_CreatedAt",
                schema: "research",
                table: "WalkForwardRuns",
                columns: new[] { "ScheduleSha256", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_WalkForwardWindowResults_ManifestSha256",
                schema: "research",
                table: "WalkForwardWindowResults",
                column: "ManifestSha256");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WalkForwardWindowResults",
                schema: "research");

            migrationBuilder.DropTable(
                name: "WalkForwardRuns",
                schema: "research");
        }
    }
}
