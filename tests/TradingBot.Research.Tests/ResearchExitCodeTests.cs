using TradingBot.Research;

namespace TradingBot.Research.Tests;

public sealed class ResearchExitCodeTests
{
    [Fact]
    public void ExitCodeThreeIsReservedForAcceptanceRejection()
    {
        Assert.Equal(ResearchExitCode.Success, ResearchExitCode.FromAcceptance(true));
        Assert.Equal(3, ResearchExitCode.FromAcceptance(false));
        Assert.Equal(ResearchExitCode.AcceptanceRejected,
            ResearchExitCode.FromAcceptance(false));
    }
}
