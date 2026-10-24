namespace Tharga.Team;

/// <summary>
/// How far the assistant has got with an answer: the text so far, which call to the model it is on, and the tools it
/// is running.
/// </summary>
/// <remarks>
/// <b>A round is one call to the model.</b> The model either writes or asks for tools; their results going back start
/// the next round, so an answer that looks something up takes two or more.
/// </remarks>
public sealed record SupportAssistantProgress
{
    /// <summary>The answer as written so far, across every round; empty until the model writes.</summary>
    public string AnswerSoFar { get; init; } = string.Empty;

    /// <summary>Which call to the model this is, from 1.</summary>
    public int Round { get; init; } = 1;

    /// <summary>The tools the model asked for that are running now; empty while it thinks or writes.</summary>
    public string[] Tools { get; init; } = [];
}
