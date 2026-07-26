using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddWalkForwardBenchmark : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WalkForwardWindowResults_Drawdown",
                schema: "research",
                table: "WalkForwardWindowResults");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WalkForwardRuns_WindowCounts",
                schema: "research",
                table: "WalkForwardRuns");

            migrationBuilder.AddColumn<decimal>(
                name: "BenchmarkAllocatedQuoteBalance",
                schema: "research",
                table: "WalkForwardWindowResults",
                type: "decimal(38,18)",
                precision: 38,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BenchmarkBaseQuantity",
                schema: "research",
                table: "WalkForwardWindowResults",
                type: "decimal(38,18)",
                precision: 38,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BenchmarkCandleCount",
                schema: "research",
                table: "WalkForwardWindowResults",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BenchmarkEndingCashBalance",
                schema: "research",
                table: "WalkForwardWindowResults",
                type: "decimal(38,18)",
                precision: 38,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BenchmarkEntryAt",
                schema: "research",
                table: "WalkForwardWindowResults",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BenchmarkEntryPrice",
                schema: "research",
                table: "WalkForwardWindowResults",
                type: "decimal(38,18)",
                precision: 38,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BenchmarkEstimatedSlippageCost",
                schema: "research",
                table: "WalkForwardWindowResults",
                type: "decimal(38,18)",
                precision: 38,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BenchmarkEstimatedSpreadCost",
                schema: "research",
                table: "WalkForwardWindowResults",
                type: "decimal(38,18)",
                precision: 38,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "BenchmarkExitAt",
                schema: "research",
                table: "WalkForwardWindowResults",
                type: "datetimeoffset(7)",
                precision: 7,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BenchmarkExitPrice",
                schema: "research",
                table: "WalkForwardWindowResults",
                type: "decimal(38,18)",
                precision: 38,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BenchmarkGrossReturnPercent",
                schema: "research",
                table: "WalkForwardWindowResults",
                type: "decimal(38,18)",
                precision: 38,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BenchmarkMaximumDrawdownPercent",
                schema: "research",
                table: "WalkForwardWindowResults",
                type: "decimal(38,18)",
                precision: 38,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BenchmarkNetLiquidationValue",
                schema: "research",
                table: "WalkForwardWindowResults",
                type: "decimal(38,18)",
                precision: 38,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BenchmarkNetReturnPercent",
                schema: "research",
                table: "WalkForwardWindowResults",
                type: "decimal(38,18)",
                precision: 38,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BenchmarkTotalFees",
                schema: "research",
                table: "WalkForwardWindowResults",
                type: "decimal(38,18)",
                precision: 38,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BenchmarkOutperformedWindowCount",
                schema: "research",
                table: "WalkForwardRuns",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "CompoundedBenchmarkNetReturnPercent",
                schema: "research",
                table: "WalkForwardRuns",
                type: "decimal(38,18)",
                precision: 38,
                scale: 18,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "MeanExcessNetReturnPercent",
                schema: "research",
                table: "WalkForwardRuns",
                type: "decimal(38,18)",
                precision: 38,
                scale: 18,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_WalkForwardWindowResults_Benchmark",
                schema: "research",
                table: "WalkForwardWindowResults",
                sql: "([BenchmarkAllocatedQuoteBalance] IS NULL AND [BenchmarkEndingCashBalance] IS NULL AND [BenchmarkBaseQuantity] IS NULL AND [BenchmarkEntryPrice] IS NULL AND [BenchmarkExitPrice] IS NULL AND [BenchmarkNetLiquidationValue] IS NULL AND [BenchmarkGrossReturnPercent] IS NULL AND [BenchmarkNetReturnPercent] IS NULL AND [BenchmarkTotalFees] IS NULL AND [BenchmarkEstimatedSpreadCost] IS NULL AND [BenchmarkEstimatedSlippageCost] IS NULL AND [BenchmarkMaximumDrawdownPercent] IS NULL AND [BenchmarkCandleCount] IS NULL AND [BenchmarkEntryAt] IS NULL AND [BenchmarkExitAt] IS NULL) OR ([BenchmarkAllocatedQuoteBalance] IS NOT NULL AND [BenchmarkEndingCashBalance] IS NOT NULL AND [BenchmarkBaseQuantity] IS NOT NULL AND [BenchmarkEntryPrice] IS NOT NULL AND [BenchmarkExitPrice] IS NOT NULL AND [BenchmarkNetLiquidationValue] IS NOT NULL AND [BenchmarkGrossReturnPercent] IS NOT NULL AND [BenchmarkNetReturnPercent] IS NOT NULL AND [BenchmarkTotalFees] IS NOT NULL AND [BenchmarkEstimatedSpreadCost] IS NOT NULL AND [BenchmarkEstimatedSlippageCost] IS NOT NULL AND [BenchmarkMaximumDrawdownPercent] IS NOT NULL AND [BenchmarkCandleCount] IS NOT NULL AND [BenchmarkEntryAt] IS NOT NULL AND [BenchmarkExitAt] IS NOT NULL AND [BenchmarkAllocatedQuoteBalance] > 0 AND [BenchmarkAllocatedQuoteBalance] <= [InitialQuoteBalance] AND [BenchmarkEndingCashBalance] = [InitialQuoteBalance] - [BenchmarkAllocatedQuoteBalance] AND [BenchmarkBaseQuantity] > 0 AND [BenchmarkEntryPrice] > 0 AND [BenchmarkExitPrice] > 0 AND [BenchmarkNetLiquidationValue] >= 0 AND [BenchmarkTotalFees] >= 0 AND [BenchmarkEstimatedSpreadCost] >= 0 AND [BenchmarkEstimatedSlippageCost] >= 0 AND [BenchmarkCandleCount] > 0 AND [BenchmarkEntryAt] = [ValidationEndExclusive] AND [BenchmarkExitAt] = [OutOfSampleEndExclusive])");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WalkForwardWindowResults_Drawdown",
                schema: "research",
                table: "WalkForwardWindowResults",
                sql: "[MaximumDrawdownPercent] >= 0 AND [MaximumDrawdownPercent] <= 100 AND ([BenchmarkMaximumDrawdownPercent] IS NULL OR ([BenchmarkMaximumDrawdownPercent] >= 0 AND [BenchmarkMaximumDrawdownPercent] <= 100))");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WalkForwardRuns_WindowCounts",
                schema: "research",
                table: "WalkForwardRuns",
                sql: "[WindowCount] > 0 AND [ProfitableWindowCount] >= 0 AND [ProfitableWindowCount] <= [WindowCount] AND (([BenchmarkOutperformedWindowCount] IS NULL AND [MeanExcessNetReturnPercent] IS NULL AND [CompoundedBenchmarkNetReturnPercent] IS NULL) OR ([BenchmarkOutperformedWindowCount] IS NOT NULL AND [MeanExcessNetReturnPercent] IS NOT NULL AND [CompoundedBenchmarkNetReturnPercent] IS NOT NULL AND [BenchmarkOutperformedWindowCount] >= 0 AND [BenchmarkOutperformedWindowCount] <= [WindowCount]))");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_WalkForwardWindowResults_Benchmark",
                schema: "research",
                table: "WalkForwardWindowResults");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WalkForwardWindowResults_Drawdown",
                schema: "research",
                table: "WalkForwardWindowResults");

            migrationBuilder.DropCheckConstraint(
                name: "CK_WalkForwardRuns_WindowCounts",
                schema: "research",
                table: "WalkForwardRuns");

            migrationBuilder.DropColumn(
                name: "BenchmarkAllocatedQuoteBalance",
                schema: "research",
                table: "WalkForwardWindowResults");

            migrationBuilder.DropColumn(
                name: "BenchmarkBaseQuantity",
                schema: "research",
                table: "WalkForwardWindowResults");

            migrationBuilder.DropColumn(
                name: "BenchmarkCandleCount",
                schema: "research",
                table: "WalkForwardWindowResults");

            migrationBuilder.DropColumn(
                name: "BenchmarkEndingCashBalance",
                schema: "research",
                table: "WalkForwardWindowResults");

            migrationBuilder.DropColumn(
                name: "BenchmarkEntryAt",
                schema: "research",
                table: "WalkForwardWindowResults");

            migrationBuilder.DropColumn(
                name: "BenchmarkEntryPrice",
                schema: "research",
                table: "WalkForwardWindowResults");

            migrationBuilder.DropColumn(
                name: "BenchmarkEstimatedSlippageCost",
                schema: "research",
                table: "WalkForwardWindowResults");

            migrationBuilder.DropColumn(
                name: "BenchmarkEstimatedSpreadCost",
                schema: "research",
                table: "WalkForwardWindowResults");

            migrationBuilder.DropColumn(
                name: "BenchmarkExitAt",
                schema: "research",
                table: "WalkForwardWindowResults");

            migrationBuilder.DropColumn(
                name: "BenchmarkExitPrice",
                schema: "research",
                table: "WalkForwardWindowResults");

            migrationBuilder.DropColumn(
                name: "BenchmarkGrossReturnPercent",
                schema: "research",
                table: "WalkForwardWindowResults");

            migrationBuilder.DropColumn(
                name: "BenchmarkMaximumDrawdownPercent",
                schema: "research",
                table: "WalkForwardWindowResults");

            migrationBuilder.DropColumn(
                name: "BenchmarkNetLiquidationValue",
                schema: "research",
                table: "WalkForwardWindowResults");

            migrationBuilder.DropColumn(
                name: "BenchmarkNetReturnPercent",
                schema: "research",
                table: "WalkForwardWindowResults");

            migrationBuilder.DropColumn(
                name: "BenchmarkTotalFees",
                schema: "research",
                table: "WalkForwardWindowResults");

            migrationBuilder.DropColumn(
                name: "BenchmarkOutperformedWindowCount",
                schema: "research",
                table: "WalkForwardRuns");

            migrationBuilder.DropColumn(
                name: "CompoundedBenchmarkNetReturnPercent",
                schema: "research",
                table: "WalkForwardRuns");

            migrationBuilder.DropColumn(
                name: "MeanExcessNetReturnPercent",
                schema: "research",
                table: "WalkForwardRuns");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WalkForwardWindowResults_Drawdown",
                schema: "research",
                table: "WalkForwardWindowResults",
                sql: "[MaximumDrawdownPercent] >= 0 AND [MaximumDrawdownPercent] <= 100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_WalkForwardRuns_WindowCounts",
                schema: "research",
                table: "WalkForwardRuns",
                sql: "[WindowCount] > 0 AND [ProfitableWindowCount] >= 0 AND [ProfitableWindowCount] <= [WindowCount]");
        }
    }
}
