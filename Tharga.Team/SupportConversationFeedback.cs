namespace Tharga.Team;

/// <summary>
/// An assistant conversation as its person rated it, handed to every <see cref="ISupportConversationFeedbackSink"/>.
/// </summary>
/// <remarks>
/// <b>What the person can see, and nothing more.</b> It carries the conversation's messages and, for the assistant's
/// answers, which tools were called and whether each succeeded. It never carries what a tool was asked for or what it
/// returned, which can hold data the person did not choose to send, nor anything written for support's eyes only.
/// </remarks>
public sealed record SupportConversationFeedback
{
    /// <summary>The support case the conversation is.</summary>
    public required string CaseId { get; init; }

    /// <summary>The team the case belongs to.</summary>
    public required string TeamKey { get; init; }

    /// <summary><see cref="SupportAnswerRating.Up"/> or <see cref="SupportAnswerRating.Down"/>.</summary>
    public required SupportAnswerRating Rating { get; init; }

    /// <summary>Why, in the person's own words, or <c>null</c> when they gave no reason.</summary>
    public string Comment { get; init; }

    /// <summary>When the conversation was rated, in UTC.</summary>
    public required DateTime RatedAt { get; init; }

    /// <summary>The culture the person was using when they rated, such as <c>sv-SE</c>; empty for the invariant culture.</summary>
    public string Culture { get; init; } = string.Empty;

    /// <summary>The conversation, oldest first.</summary>
    public SupportConversationFeedbackMessage[] Messages { get; init; } = [];
}
