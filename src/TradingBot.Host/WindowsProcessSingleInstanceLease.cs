using System.Runtime.Versioning;

namespace TradingBot.Host;

[SupportedOSPlatform("windows")]
public sealed class WindowsProcessSingleInstanceLease : IDisposable
{
    private readonly EventWaitHandle _handle;

    private WindowsProcessSingleInstanceLease(EventWaitHandle handle) =>
        _handle = handle;

    public static WindowsProcessSingleInstanceLease Acquire(string identity)
    {
        ValidateIdentity(identity);
        var objectName = $"Global\\TradingBot.Host.{identity}";
        EventWaitHandle handle;
        bool createdNew;
        try
        {
            handle = new EventWaitHandle(
                initialState: false,
                EventResetMode.ManualReset,
                objectName,
                out createdNew);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new InvalidOperationException(
                "Another Windows process owns the configured TradingBot service identity.",
                exception);
        }

        if (createdNew)
        {
            return new WindowsProcessSingleInstanceLease(handle);
        }

        handle.Dispose();
        throw new InvalidOperationException(
            "Another Windows process owns the configured TradingBot service identity.");
    }

    public void Dispose() => _handle.Dispose();

    private static void ValidateIdentity(string identity)
    {
        if (string.IsNullOrWhiteSpace(identity) || identity.Length > 64)
        {
            throw new ArgumentException(
                "Windows service identity must contain 1-64 safe characters.",
                nameof(identity));
        }

        foreach (var character in identity)
        {
            if (!char.IsAsciiLetterOrDigit(character) &&
                character is not '.' and not '-' and not '_')
            {
                throw new ArgumentException(
                    "Windows service identity contains an unsafe character.",
                    nameof(identity));
            }
        }
    }
}
