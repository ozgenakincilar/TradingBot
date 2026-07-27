using System.Runtime.Versioning;

namespace TradingBot.Host.Tests;

public sealed class WindowsProcessSingleInstanceLeaseTests
{
    [Fact]
    [SupportedOSPlatform("windows")]
    public void Acquire_rejects_second_owner_and_recovers_after_dispose()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var identity = $"test-{Guid.NewGuid():N}";
        using (WindowsProcessSingleInstanceLease.Acquire(identity))
        {
            var exception = Assert.Throws<InvalidOperationException>(
                () => WindowsProcessSingleInstanceLease.Acquire(identity));
            Assert.Contains("Another Windows process", exception.Message,
                StringComparison.Ordinal);
        }

        using var recovered = WindowsProcessSingleInstanceLease.Acquire(identity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("unsafe/name")]
    [InlineData("unsafe\\name")]
    [SupportedOSPlatform("windows")]
    public void Acquire_rejects_unsafe_identity(string identity)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Assert.Throws<ArgumentException>(
            () => WindowsProcessSingleInstanceLease.Acquire(identity));
    }
}
