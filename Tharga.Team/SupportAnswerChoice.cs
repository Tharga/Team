namespace Tharga.Team;

/// <summary>What the person answering chose for one answer: the address it is sent from, and whether it is signed with their name.</summary>
public sealed record SupportAnswerChoice
{
    /// <summary>The address to send from, one of <see cref="SupportAnswerRoute.Senders"/>; null uses <see cref="SupportAnswerRoute.From"/>.</summary>
    public string SendFrom { get; init; }

    /// <summary>Whether the answer is signed with the answerer's name; null uses the host's default.</summary>
    public bool? SignWithName { get; init; }

    /// <summary>
    /// Answer a case that has no mail thread by mail to the address the host resolves for it (<see cref="ISupportCaseContactResolver"/>), sent from
    /// <see cref="SendFrom"/>; the case is then bound to the mail channel with that address and mailbox and continues as a mail thread. Refused when the
    /// host resolves no address. Has no effect on a case that already has one.
    /// </summary>
    public bool ByMail { get; init; }
}
