namespace Tharga.Team;

/// <summary>
/// What an assistant had to say about a case.
/// </summary>
/// <remarks>
/// A record with no <c>Body</c> is a decline, and <see cref="Reason"/> says why in terms a person reading the
/// case can use. Both outcomes are ordinary, so neither throws.
/// </remarks>
public record SupportAnswer
{
    /// <summary>The answer to append to the case, or <c>null</c> when the assistant declined.</summary>
    public string Body { get; init; }

    /// <summary>Why the assistant declined, or <c>null</c> when it answered.</summary>
    public string Reason { get; init; }

    /// <summary>
    /// What the person asked for that the assistant had no way to do, in its own words, whether it answered or not.
    /// </summary>
    /// <remarks>Kept on the case for the people who build the assistant, as <see cref="SupportCase.AssistantGaps"/>.</remarks>
    public string[] Gaps { get; init; } = [];

    /// <summary>The service calls the assistant made to answer, in order; kept with the answer as <see cref="SupportMessage.AssistantCalls"/>.</summary>
    public SupportAssistantCall[] Calls { get; init; } = [];

    /// <summary>True when there is an answer to append.</summary>
    public bool Answered => !string.IsNullOrWhiteSpace(Body);

    /// <summary>An answer.</summary>
    public static SupportAnswer FromBody(string body) => new() { Body = body };

    /// <summary>A decline, with a reason a person can read.</summary>
    public static SupportAnswer Declined(string reason) => new() { Reason = reason };
}
