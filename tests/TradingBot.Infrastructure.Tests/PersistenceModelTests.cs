using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using TradingBot.Infrastructure.Persistence;
using TradingBot.Infrastructure.Persistence.Entities;

namespace TradingBot.Infrastructure.Tests;

public sealed class PersistenceModelTests
{
    private const string DesignOnlyConnectionString =
        "Server=unused;Database=unused;Integrated Security=True;TrustServerCertificate=True";

    [Theory]
    [InlineData(typeof(ExecutionOrderEntity), "execution", "Orders")]
    [InlineData(typeof(RiskDecisionEntity), "risk", "RiskDecisions")]
    [InlineData(typeof(OutboxMessageEntity), "operations", "OutboxMessages")]
    [InlineData(typeof(AuditEventEntity), "operations", "AuditEvents")]
    [InlineData(typeof(AssetBalanceEntity), "portfolio", "AssetBalances")]
    [InlineData(typeof(SpotPositionEntity), "portfolio", "SpotPositions")]
    [InlineData(typeof(SpotExecutionEntity), "portfolio", "SpotExecutions")]
    [InlineData(typeof(SpotOrderReservationEntity), "portfolio", "SpotOrderReservations")]
    [InlineData(typeof(ReconciliationRunEntity), "operations", "ReconciliationRuns")]
    [InlineData(typeof(TradingSafetyStateEntity), "operations", "TradingSafetyStates")]
    [InlineData(typeof(TradingSafetyRecoveryEntity), "operations", "TradingSafetyRecoveries")]
    [InlineData(typeof(WalkForwardRunEntity), "research", "WalkForwardRuns")]
    [InlineData(typeof(WalkForwardWindowResultEntity), "research", "WalkForwardWindowResults")]
    public void Model_MapsEntitiesToExpectedSchemas(Type entityType, string schema, string table)
    {
        using var context = CreateContext();
        var modelEntity = context.Model.FindEntityType(entityType);

        Assert.NotNull(modelEntity);
        Assert.Equal(schema, modelEntity.GetSchema());
        Assert.Equal(table, modelEntity.GetTableName());
    }

    [Fact]
    public void Orders_ClientOrderIdIndexIsUnique()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(ExecutionOrderEntity));
        var property = entity?.FindProperty(nameof(ExecutionOrderEntity.ClientOrderId));
        var index = property is null ? null : entity?.FindIndex(property);

        Assert.NotNull(index);
        Assert.True(index.IsUnique);
    }

    [Theory]
    [InlineData(nameof(ExecutionOrderEntity.RequestedQuantity))]
    [InlineData(nameof(ExecutionOrderEntity.ApprovedQuantity))]
    [InlineData(nameof(ExecutionOrderEntity.FilledQuantity))]
    [InlineData(nameof(ExecutionOrderEntity.AverageFillPrice))]
    public void Orders_FinancialColumnsUseRequiredPrecision(string propertyName)
    {
        using var context = CreateContext();
        var property = context.Model
            .FindEntityType(typeof(ExecutionOrderEntity))?
            .FindProperty(propertyName);

        Assert.NotNull(property);
        Assert.Equal(38, property.GetPrecision());
        Assert.Equal(18, property.GetScale());
    }

    [Theory]
    [InlineData(typeof(ExecutionOrderEntity), nameof(ExecutionOrderEntity.RowVersion))]
    [InlineData(typeof(OutboxMessageEntity), nameof(OutboxMessageEntity.RowVersion))]
    [InlineData(typeof(AssetBalanceEntity), nameof(AssetBalanceEntity.RowVersion))]
    [InlineData(typeof(SpotPositionEntity), nameof(SpotPositionEntity.RowVersion))]
    [InlineData(typeof(SpotOrderReservationEntity), nameof(SpotOrderReservationEntity.RowVersion))]
    [InlineData(typeof(TradingSafetyStateEntity), nameof(TradingSafetyStateEntity.RowVersion))]
    public void ConcurrencyColumns_AreConfiguredAsRowVersion(Type entityType, string propertyName)
    {
        using var context = CreateContext();
        var property = context.Model.FindEntityType(entityType)?.FindProperty(propertyName);

        Assert.NotNull(property);
        Assert.True(property.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.OnAddOrUpdate, property.ValueGenerated);
        Assert.Equal("rowversion", property.GetColumnType());
    }

    private static TradingBotDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<TradingBotDbContext>()
            .UseSqlServer(DesignOnlyConnectionString)
            .Options;

        return new TradingBotDbContext(options);
    }
}
