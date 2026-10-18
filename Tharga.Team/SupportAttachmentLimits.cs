namespace Tharga.Team;

/// <summary>How many files, and how large, one support message may carry, applied the same way in and out.</summary>
public record SupportAttachmentLimits
{
    /// <summary>The largest one file may be, in bytes.</summary>
    public required long MaxFileBytes { get; init; }

    /// <summary>The most all files on one message may add up to, in bytes.</summary>
    public required long MaxTotalBytes { get; init; }

    /// <summary>The most files one message may carry.</summary>
    public required int MaxCount { get; init; }

    /// <summary>25 MB a file, 50 MB a message and 20 files.</summary>
    public static SupportAttachmentLimits Default { get; } = new()
    {
        MaxFileBytes = 25L * 1024 * 1024,
        MaxTotalBytes = 50L * 1024 * 1024,
        MaxCount = 20
    };
}
