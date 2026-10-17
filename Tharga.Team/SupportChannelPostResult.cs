namespace Tharga.Team;

/// <summary>
/// What became of posting one message into a channel.
/// </summary>
/// <param name="Delivered">True when the channel accepted it.</param>
/// <param name="Error">Why it did not, as the channel put it, or null when it did or cannot say.</param>
public sealed record SupportChannelPostResult(bool Delivered, string Error = null)
{
    /// <summary>The channel accepted the message.</summary>
    public static SupportChannelPostResult Ok() => new(true);

    /// <summary>It did not, for the stated reason.</summary>
    public static SupportChannelPostResult Failed(string error) => new(false, error);
}
