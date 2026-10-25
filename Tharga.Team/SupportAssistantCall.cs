namespace Tharga.Team;

/// <summary>
/// One service call the assistant made while answering: the tool, and what it was asked for.
/// </summary>
/// <remarks>
/// Kept with the answer as <see cref="SupportMessage.AssistantCalls"/>, so the person answered and support can see how
/// the assistant came to it. What it was asked for is what the model passed, which can hold words from the conversation;
/// it is shown only where the conversation itself can be read.
/// </remarks>
public sealed record SupportAssistantCall
{
    /// <summary>The tool's name, such as <c>search_my_cases</c>.</summary>
    public required string Name { get; init; }

    /// <summary>What the tool was asked for, written out short, such as <c>text: "export"</c>; empty when it takes nothing.</summary>
    public string Arguments { get; init; } = string.Empty;
}
