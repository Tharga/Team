namespace Tharga.Team;

/// <summary>
/// Somebody is writing in a support case and has not sent it yet.
/// </summary>
/// <remarks>
/// <b>Who and where, never what.</b> Like <see cref="SupportCaseUpdatedEventArgs"/> it carries no text: a
/// draft is less settled than a message, and is just as likely to hold something pasted by mistake.
/// <para>
/// <b>There is no "stopped".</b> A signal means "writing now" and lapses on its own when no further one
/// follows, so a writer who closes the page or loses the connection never leaves the other side looking at
/// dots. A message arriving on the case ends it too.
/// </para>
/// </remarks>
public sealed class SupportCaseTypingEventArgs : EventArgs
{
    public required string TeamKey { get; init; }

    public required string CaseId { get; init; }

    /// <summary>Who is writing, as <see cref="SupportMessage.AuthorIdentity"/> would record them.</summary>
    public required string Identity { get; init; }

    /// <summary>Their name, as a message from them would show it.</summary>
    public string Name { get; init; }

    /// <summary>Writing an answer as support rather than as a member of the case's team.</summary>
    public required bool AsSupport { get; init; }
}
