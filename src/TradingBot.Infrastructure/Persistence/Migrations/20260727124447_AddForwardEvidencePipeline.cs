using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TradingBot.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddForwardEvidencePipeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ForwardEvidenceArtifacts",
                schema: "research",
                columns: table => new
                {
                    WindowSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    PipelineId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    WindowIndex = table.Column<int>(type: "int", nullable: false),
                    StartInclusive = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    EndExclusive = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    ManifestPath = table.Column<string>(type: "varchar(2048)", unicode: false, maxLength: 2048, nullable: false),
                    ManifestSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    SignalPath = table.Column<string>(type: "varchar(2048)", unicode: false, maxLength: 2048, nullable: false),
                    SignalSourceId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    SignalSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    SignalCandleCount = table.Column<long>(type: "bigint", nullable: false),
                    SignalTimeframeSeconds = table.Column<long>(type: "bigint", nullable: false),
                    TrendPath = table.Column<string>(type: "varchar(2048)", unicode: false, maxLength: 2048, nullable: false),
                    TrendSourceId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    TrendSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    TrendCandleCount = table.Column<long>(type: "bigint", nullable: false),
                    TrendTimeframeSeconds = table.Column<long>(type: "bigint", nullable: false),
                    SealedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForwardEvidenceArtifacts", x => x.WindowSha256);
                    table.CheckConstraint("CK_ForwardEvidenceArtifacts_Counts", "[SignalCandleCount] = 2880 AND [TrendCandleCount] = 720 AND [SignalTimeframeSeconds] = 900 AND [TrendTimeframeSeconds] = 3600");
                    table.CheckConstraint("CK_ForwardEvidenceArtifacts_Window", "[WindowIndex] >= 0 AND [EndExclusive] > [StartInclusive] AND [SealedAt] >= [EndExclusive]");
                });

            migrationBuilder.CreateTable(
                name: "ForwardEvidenceEvaluations",
                schema: "research",
                columns: table => new
                {
                    RunSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    PipelineId = table.Column<string>(type: "varchar(128)", unicode: false, maxLength: 128, nullable: false),
                    SealedWindowCount = table.Column<int>(type: "int", nullable: false),
                    ReportSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    ReportPath = table.Column<string>(type: "varchar(2048)", unicode: false, maxLength: 2048, nullable: false),
                    ReportFileSha256 = table.Column<string>(type: "char(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    EvaluatedAt = table.Column<DateTimeOffset>(type: "datetimeoffset(7)", precision: 7, nullable: false),
                    MinimumTradesPassed = table.Column<bool>(type: "bit", nullable: false),
                    ProfitFactorPassed = table.Column<bool>(type: "bit", nullable: false),
                    PositiveNetReturnPassed = table.Column<bool>(type: "bit", nullable: false),
                    BenchmarkExcessPassed = table.Column<bool>(type: "bit", nullable: false),
                    DrawdownPassed = table.Column<bool>(type: "bit", nullable: false),
                    ProfitableWindowsPassed = table.Column<bool>(type: "bit", nullable: false),
                    ExecutionCostCoveragePassed = table.Column<bool>(type: "bit", nullable: false),
                    FullyExecutedPassed = table.Column<bool>(type: "bit", nullable: false),
                    IsAccepted = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ForwardEvidenceEvaluations", x => x.RunSha256);
                    table.CheckConstraint("CK_ForwardEvidenceEvaluations_Consistency", "[SealedWindowCount] >= 7 AND [IsAccepted] = ([MinimumTradesPassed] & [ProfitFactorPassed] & [PositiveNetReturnPassed] & [BenchmarkExcessPassed] & [DrawdownPassed] & [ProfitableWindowsPassed] & [ExecutionCostCoveragePassed] & [FullyExecutedPassed])");
                });

            migrationBuilder.CreateIndex(
                name: "IX_ForwardEvidenceArtifacts_PipelineId_WindowIndex",
                schema: "research",
                table: "ForwardEvidenceArtifacts",
                columns: new[] { "PipelineId", "WindowIndex" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ForwardEvidenceEvaluations_PipelineId_SealedWindowCount",
                schema: "research",
                table: "ForwardEvidenceEvaluations",
                columns: new[] { "PipelineId", "SealedWindowCount" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ForwardEvidenceEvaluations_ReportSha256",
                schema: "research",
                table: "ForwardEvidenceEvaluations",
                column: "ReportSha256",
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER [research].[TR_ForwardEvidenceArtifacts_AppendOnly]
                ON [research].[ForwardEvidenceArtifacts]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, 'Forward evidence artifacts are append-only.', 1;
                END;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER [research].[TR_ForwardEvidenceEvaluations_AppendOnly]
                ON [research].[ForwardEvidenceEvaluations]
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51001, 'Forward evidence evaluations are append-only.', 1;
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ForwardEvidenceArtifacts",
                schema: "research");

            migrationBuilder.DropTable(
                name: "ForwardEvidenceEvaluations",
                schema: "research");
        }
    }
}
