namespace Tharga.Team;

/// <summary>
/// One entry of a rated conversation in <see cref="SupportConversationFeedback"/>.
/// </summary>
public sealed record SupportConversationFeedbackMessage
{
    /// <summary>Position in the case's transcript, from 1.</summary>
    public required int Sequence { get; init; }

    /// <summary>Whether a person, the assistant or the toolkit wrote it.</summary>
    public required SupportMessageKind Kind { get; init; }

    /// <summary>Written from the support side, so a person's answer after a hand-over is told apart from the question.</summary>
    public bool AsSupport { get; init; }

    /// <summary>When it was written, in UTC.</summary>
    public required DateTime SentAt { get; init; }

    /// <summary>The entry as plain text.</summary>
    public required string Body { get; init; }

    /// <summary>The tools the assistant called for this answer, in order; empty for any other entry.</summary>
    public SupportConversationFeedbackCall[] Calls { get; init; } = [];
}
