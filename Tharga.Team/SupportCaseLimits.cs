namespace Tharga.Team;

/// <summary>
/// What bounds a support case, so an embedded transcript cannot grow without limit.
/// </summary>
/// <remarks>
/// <b>These are contract-level facts, not storage tuning.</b> A caller needs to know a reply can be refused
/// for length before it writes a UI that lets someone type past it, so the numbers live beside the contracts
/// rather than inside the adapter.
/// <para>
/// The pair is chosen together against MongoDB's 16 MB document limit: at
/// <see cref="MaxMessagesPerCase"/> messages of <see cref="MaxMessageLength"/> characters the transcript is
/// roughly 5 MB, leaving substantial headroom for a case that is entirely maximum-length messages.
/// </para>
/// </remarks>
public static class SupportCaseLimits
{
    /// <summary>
    /// Longest message body, in characters.
    /// </summary>
    /// <remarks>
    /// Support text is where somebody pastes a log file or a stack trace, so this is the cap that actually
    /// gets hit. Refusing a long message with a clear error is better than accepting one that makes the case
    /// unreadable.
    /// </remarks>
    public const int MaxMessageLength = 10_000;

    /// <summary>
    /// Most entries one case may hold, including system entries.
    /// </summary>
    /// <remarks>
    /// A real support conversation runs to tens of messages. A case reaching this many has stopped being one
    /// conversation, so the store refuses further replies and the remedy is a new case — which is also the
    /// honest outcome, since nobody reads a thousand-message thread.
    /// </remarks>
    public const int MaxMessagesPerCase = 500;

    /// <summary>
    /// How much of the message becomes the subject when a case is raised without one.
    /// </summary>
    /// <remarks>
    /// Long enough to carry a recognisable sentence in a list, short enough that a list of them stays
    /// scannable. The cut lands on a word boundary, so a derived subject is usually a little shorter than
    /// this.
    /// </remarks>
    public const int DerivedSubjectLength = 50;
}
