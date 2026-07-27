namespace TradingBot.Host;

public sealed class ForwardEvidenceSingleInstanceLease : IDisposable
{
    private readonly FileStream _stream;

    private ForwardEvidenceSingleInstanceLease(FileStream stream) => _stream = stream;

    public static ForwardEvidenceSingleInstanceLease Acquire(string evidenceRootPath)
    {
        if (string.IsNullOrWhiteSpace(evidenceRootPath))
        {
            throw new ArgumentException("Forward evidence root path is required.",
                nameof(evidenceRootPath));
        }

        var root = Path.GetFullPath(evidenceRootPath);
        Directory.CreateDirectory(root);
        var lockPath = Path.Combine(root, ".writer.lock");
        try
        {
            var stream = new FileStream(
                lockPath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.None);
            return new ForwardEvidenceSingleInstanceLease(stream);
        }
        catch (IOException exception)
        {
            throw new InvalidOperationException(
                "Another forward evidence writer already owns the configured storage root.",
                exception);
        }
    }

    public void Dispose() => _stream.Dispose();
}
