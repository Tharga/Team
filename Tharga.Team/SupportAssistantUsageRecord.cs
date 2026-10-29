namespace Tharga.Team;

/// <summary>
/// One time the support assistant was asked: who asked, in which case, which model answered, and what it took.
/// </summary>
/// <remarks>
/// <b>Never what anybody wrote.</b> Not the question, not the answer, not a tool's arguments: a support conversation is
/// free-form text and where a password gets pasted, and a usage record is read by whoever bills, not by support.
/// <para>
/// Kept whether the assistant answered or declined, because a decline spent the tokens too.
/// </para>
/// </remarks>
public sealed record SupportAssistantUsageRecord
{
    /// <summary>The team the case belongs to.</summary>
    public required string TeamKey { get; init; }

    /// <summary>The case the assistant was asked in.</summary>
    public required string CaseId { get; init; }

    /// <summary>The identity of whoever the assistant was answering; null when there was no caller.</summary>
    public string AskedBy { get; init; }

    /// <summary>Who served the answer, as the chat client names its provider; null when it does not say.</summary>
    public string Provider { get; init; }

    /// <summary>The model that answered; null when neither the provider nor the client says.</summary>
    public string Model { get; init; }

    /// <summary>The tokens the model read.</summary>
    public long InputTokens { get; init; }

    /// <summary>Of <see cref="InputTokens"/>, those read from the provider's cache.</summary>
    public long CachedInputTokens { get; init; }

    /// <summary>The tokens the model wrote.</summary>
    public long OutputTokens { get; init; }

    /// <summary>All of them, as the provider totalled them.</summary>
    public long TotalTokens { get; init; }

    /// <summary>How many times the assistant called a service to answer.</summary>
    public int ToolCalls { get; init; }

    /// <summary>Whether it answered; false when it declined.</summary>
    public bool Answered { get; init; }

    /// <summary>How long the assistant took, from being asked to having answered or declined.</summary>
    public TimeSpan Duration { get; init; }

    /// <summary>When it finished, in UTC.</summary>
    public DateTime At { get; init; }
}
