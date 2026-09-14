namespace Tharga.Team.Support.Cases;

/// <summary>
/// How the assistant behaves when one is available.
/// </summary>
/// <remarks>
/// <b>No model, no endpoint, no key.</b> Those belong to the <c>IChatClient</c> the host registers, which is
/// what keeps the choice of provider — a local model, a self-hosted one, or a hosted API — out of the toolkit
/// entirely.
/// </remarks>
public class SupportAssistantOptions
{
    /// <summary>
    /// What the assistant is told about its job, prepended to every case it answers.
    /// </summary>
    /// <remarks>
    /// The default says only what is true of every host: answer from the tools, and say so rather than guess.
    /// A host that knows its own product should replace it.
    /// </remarks>
    public string Instructions { get; set; } =
        "You answer support questions for the signed-in user. " +
        "Use the supplied tools to look up their teams, membership and cases rather than assuming. " +
        "If you cannot answer from what the tools return, say so plainly and tell them a person will pick it up. " +
        "Never guess at account state, and never claim to have changed anything: you can read, not act.";

    /// <summary>
    /// How long the assistant gets before the case is left to a person.
    /// </summary>
    /// <remarks>
    /// A customer waiting on an answer is the thing being protected here. A local model on a cold start can
    /// take a while, so this is generous rather than tight — but it is bounded, because a request that never
    /// returns is worse than one that declines.
    /// </remarks>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// How many transcript entries the assistant is given.
    /// </summary>
    /// <remarks>
    /// Bounded because a transcript is unbounded and a context window is not. The newest entries are kept:
    /// the question being asked now matters more than how the case opened.
    /// </remarks>
    public int TranscriptLimit { get; set; } = 20;
}
