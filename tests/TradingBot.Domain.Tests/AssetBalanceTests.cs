using TradingBot.Domain.Common;
using TradingBot.Domain.Portfolio;

namespace TradingBot.Domain.Tests;

public sealed class AssetBalanceTests
{
    private static readonly DateTimeOffset Now = new(2026, 7, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reserve_ReducesAvailableButNotTotal()
    {
        var balance = CreateBalance(total: 100m);

        balance.Reserve(25m, Now.AddSeconds(1));

        Assert.Equal(100m, balance.Total);
        Assert.Equal(25m, balance.Reserved);
        Assert.Equal(75m, balance.Available);
    }

    [Fact]
    public void Reserve_CannotExceedAvailableBalance()
    {
        var balance = CreateBalance(total: 100m, reserved: 80m);

        var action = () => balance.Reserve(21m, Now.AddSeconds(1));

        Assert.Throws<DomainRuleViolationException>(action);
    }

    [Fact]
    public void DebitReserved_ReducesTotalAndReservationAtomically()
    {
        var balance = CreateBalance(total: 100m, reserved: 40m);

        balance.DebitReserved(30m, Now.AddSeconds(1));

        Assert.Equal(70m, balance.Total);
        Assert.Equal(10m, balance.Reserved);
        Assert.Equal(60m, balance.Available);
    }

    [Fact]
    public void Events_CannotMoveBackwardsInTime()
    {
        var balance = CreateBalance(total: 100m);

        var action = () => balance.Credit(1m, Now.AddSeconds(-1));

        Assert.Throws<DomainRuleViolationException>(action);
    }

    private static AssetBalance CreateBalance(decimal total, decimal reserved = 0m) =>
        AssetBalance.Create(AssetCode.Create("USDT"), total, reserved, Now);
}
