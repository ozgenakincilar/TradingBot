using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TradingBot.Infrastructure.Persistence;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Tests;

public sealed class ForwardEvidenceAppendOnlySqlIntegrationTests
{
    private const string ConnectionVariable =
        "TRADINGBOT_FORWARD_EVIDENCE_TEST_DB_CONNECTION";
    private const string RequiredDatabase = "TradingBotForwardEvidenceTest";

    [Fact]
    [Trait("Category", "SqlServerIntegration")]
    public async Task MigratedSqlTriggersRejectAllMutationAndTransactionRollsBackCleanly()
    {
        var connectionString = Environment.GetEnvironmentVariable(ConnectionVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        await using var context = CreateContext(connectionString);
        Assert.Equal(RequiredDatabase, context.Database.GetDbConnection().Database);
        await context.Database.MigrateAsync();

        await AssertMutationFailsClosedAsync(connectionString, Target.Artifact, delete: false);
        await AssertMutationFailsClosedAsync(connectionString, Target.Artifact, delete: true);
        await AssertMutationFailsClosedAsync(connectionString, Target.Evaluation, delete: false);
        await AssertMutationFailsClosedAsync(connectionString, Target.Evaluation, delete: true);
    }

    private static async Task AssertMutationFailsClosedAsync(
        string connectionString,
        Target target,
        bool delete)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var artifactHash = Hash($"artifact-{suffix}");
        var runHash = Hash($"run-{suffix}");
        await using var context = CreateContext(connectionString);
        await using var transaction = await context.Database.BeginTransactionAsync();
        if (target == Target.Artifact)
        {
            context.ForwardEvidenceArtifacts.Add(Artifact(
                suffix,
                artifactHash,
                $"test/{suffix}/manifest.json"));
        }
        else
        {
            context.ForwardEvidenceEvaluations.Add(Evaluation(suffix, runHash));
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        FormattableString command = (target, delete) switch
        {
            (Target.Artifact, false) =>
                $"UPDATE [research].[ForwardEvidenceArtifacts] SET [ManifestPath] = 'mutated' WHERE [WindowSha256] = {artifactHash}",
            (Target.Artifact, true) =>
                $"DELETE FROM [research].[ForwardEvidenceArtifacts] WHERE [WindowSha256] = {artifactHash}",
            (Target.Evaluation, false) =>
                $"UPDATE [research].[ForwardEvidenceEvaluations] SET [ReportPath] = 'mutated' WHERE [RunSha256] = {runHash}",
            _ =>
                $"DELETE FROM [research].[ForwardEvidenceEvaluations] WHERE [RunSha256] = {runHash}"
        };
        var exception = await Assert.ThrowsAnyAsync<DbException>(() =>
            context.Database.ExecuteSqlInterpolatedAsync(command));
        Assert.Contains("append-only", exception.Message, StringComparison.OrdinalIgnoreCase);

        var transactionState = await GetTransactionStateAsync(context);
        if (transactionState == 1)
        {
            Assert.Equal(
                1,
                target == Target.Artifact
                    ? await context.ForwardEvidenceArtifacts.CountAsync(item =>
                        item.WindowSha256 == artifactHash && item.ManifestPath != "mutated")
                    : await context.ForwardEvidenceEvaluations.CountAsync(item =>
                        item.RunSha256 == runHash && item.ReportPath != "mutated"));
        }
        else
        {
            Assert.True(transactionState is -1 or 0);
        }

        if (transactionState != 0)
        {
            await transaction.RollbackAsync();
        }

        await using var verification = CreateContext(connectionString);
        Assert.False(await verification.ForwardEvidenceArtifacts
            .AnyAsync(item => item.WindowSha256 == artifactHash));
        Assert.False(await verification.ForwardEvidenceEvaluations
            .AnyAsync(item => item.RunSha256 == runHash));
    }

    private static async Task<int> GetTransactionStateAsync(TradingBotDbContext context)
    {
        var connection = context.Database.GetDbConnection();
        await using var command = connection.CreateCommand();
        command.Transaction = context.Database.CurrentTransaction!.GetDbTransaction();
        command.CommandText = "SELECT XACT_STATE();";
        return Convert.ToInt32(await command.ExecuteScalarAsync());
    }

    private static ForwardEvidenceArtifactEntity Artifact(
        string suffix,
        string artifactHash,
        string manifestPath)
    {
        var start = new DateTimeOffset(2026, 7, 28, 0, 0, 0, TimeSpan.Zero);
        return new ForwardEvidenceArtifactEntity
        {
            WindowSha256 = artifactHash,
            PipelineId = $"sql-trigger-{suffix[..16]}",
            WindowIndex = 0,
            StartInclusive = start,
            EndExclusive = start.AddDays(30),
            ManifestPath = manifestPath,
            ManifestSha256 = Hash($"manifest-{suffix}"),
            SignalPath = $"test/{suffix}/15m.csv",
            SignalSourceId = $"signal-{suffix[..16]}",
            SignalSha256 = Hash($"signal-{suffix}"),
            SignalCandleCount = 2_880,
            SignalTimeframeSeconds = 900,
            TrendPath = $"test/{suffix}/1h.csv",
            TrendSourceId = $"trend-{suffix[..16]}",
            TrendSha256 = Hash($"trend-{suffix}"),
            TrendCandleCount = 720,
            TrendTimeframeSeconds = 3_600,
            SealedAt = start.AddDays(30)
        };
    }

    private static ForwardEvidenceEvaluationEntity Evaluation(
        string suffix,
        string runHash) => new()
        {
            RunSha256 = runHash,
            PipelineId = $"sql-trigger-{suffix[..16]}",
            SealedWindowCount = 7,
            ReportSha256 = Hash($"report-{suffix}"),
            ReportPath = $"test/{suffix}/report.json",
            ReportFileSha256 = Hash($"report-file-{suffix}"),
            EvaluatedAt = new DateTimeOffset(2027, 2, 23, 0, 0, 0, TimeSpan.Zero),
            MinimumTradesPassed = false,
            ProfitFactorPassed = false,
            PositiveNetReturnPassed = false,
            BenchmarkExcessPassed = false,
            DrawdownPassed = false,
            ProfitableWindowsPassed = false,
            ExecutionCostCoveragePassed = false,
            FullyExecutedPassed = false,
            IsAccepted = false
        };

    private static string Hash(string value) => Convert.ToHexString(
        System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(value)));

    private static TradingBotDbContext CreateContext(string connectionString)
    {
        var options = new DbContextOptionsBuilder<TradingBotDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new TradingBotDbContext(options);
    }

    private enum Target
    {
        Artifact,
        Evaluation
    }
}
