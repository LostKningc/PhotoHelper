using PhotoHelper.Models;

namespace PhotoHelper.Services;

public sealed record ArchiveResult
{
    public ArchiveResult(PhotoHistory record, bool wasCopied)
    {
        Record = record ?? throw new ArgumentNullException(nameof(record));
        WasCopied = wasCopied;
    }

    public PhotoHistory Record { get; }
    public bool WasCopied { get; }
}
