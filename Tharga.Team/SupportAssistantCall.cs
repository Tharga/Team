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

    /// <summary>
    /// Whether the call returned without failing, or <c>null</c> when that is not known, as for an answer kept before this
    /// was recorded or a responder that does not say.
    /// </summary>
    public bool? Succeeded { get; init; }

    /// <summary>
    /// What the tool handed back to the model, as text; empty when it was not kept, which is the default, and for every call kept before this existed.
    /// </summary>
    /// <remarks>
    /// <b>Opt-in, because a tool's result can hold the tenant's own data.</b> It is kept only when the host sets
    /// <c>SupportAssistantOptions.StoreCallResults</c>, and cut to <c>MaxStoredCallResultLength</c> with a marker when longer. It is never shown in the panel and
    /// never part of the conversation feedback sent to the host's sinks or mailed.
    /// </remarks>
    public string Result { get; init; } = string.Empty;
}
