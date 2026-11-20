namespace Tharga.Team;

/// <summary>
/// One tool the assistant called while answering, as <see cref="SupportConversationFeedback"/> carries it: its name and
/// whether it succeeded, never what it was asked for or what it returned.
/// </summary>
public sealed record SupportConversationFeedbackCall
{
    /// <summary>The tool's name, such as <c>search_my_cases</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Whether the call returned without failing, or <c>null</c> when that is not known.</summary>
    public bool? Succeeded { get; init; }
}
