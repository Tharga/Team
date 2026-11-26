namespace Tharga.Team.Support.Cases;

/// <summary>
/// How long the files a member hands support's staff only are kept, and how often the sweep that removes them runs.
/// </summary>
/// <remarks>
/// A secure file is one only the product's support staff and the member who uploaded it may open; see <see cref="SupportAttachment.Secure"/>.
/// Removing its content leaves the entry saying it was removed, so the conversation still reads.
/// </remarks>
public class SupportSecureFileOptions
{
    /// <summary>
    /// How long after its entry was written a secure file's content is removed. Default 30 days; <see cref="TimeSpan.Zero"/> keeps
    /// secure files until support removes them.
    /// </summary>
    /// <remarks>Zero registers no sweep at all, as <see cref="SupportCaseOptions.AutoCloseAfter"/> does.</remarks>
    public TimeSpan KeepFor { get; set; } = TimeSpan.FromDays(30);

    /// <summary>How often the sweep runs. Default one hour.</summary>
    /// <remarks>It decides only how promptly a file goes once it is due, so an hour is generous for a span measured in days.</remarks>
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Most files one sweep removes, so a large backlog cannot become one enormous pass. Default 100.</summary>
    public int SweepBatchSize { get; set; } = 100;
}
