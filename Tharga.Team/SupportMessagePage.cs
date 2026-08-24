namespace Tharga.Team;

/// <summary>
/// A page of a support case's transcript, with an explicit cursor for the next one.
/// </summary>
/// <remarks>
/// The cursor is the <see cref="SupportMessage.Sequence"/> of the last item returned, so paging is stable
/// while a conversation is still being appended to — a new reply cannot shift entries the reader already
/// passed. <see cref="NextCursor"/> is <c>null</c> when there is no further page.
/// </remarks>
public record SupportMessagePage
{
    public required SupportMessage[] Items { get; init; }

    public string NextCursor { get; init; }
}
