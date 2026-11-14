namespace Tharga.Team;

/// <summary>Where support's answer to a case goes: mailed to the address the case corresponds with, from one of support's mailboxes, or shown on the site.</summary>
public sealed record SupportAnswerRoute
{
    /// <summary>A case with no mail thread: the answer is shown on the site, where the person who raised it reads it.</summary>
    public static SupportAnswerRoute Site { get; } = new() { ByMail = false };

    /// <summary>Whether the answer is mailed; when false it is shown on the site only.</summary>
    public required bool ByMail { get; init; }

    /// <summary>The address the answer is mailed to.</summary>
    public string To { get; init; }

    /// <summary>The address it is sent from by default: the answering staff member's own when they may send as it, otherwise the address the customer wrote to or the mailbox the case came in through.</summary>
    public string From { get; init; }

    /// <summary>Every address the answering staff member may send from, <see cref="From"/> among them.</summary>
    public SupportAnswerSender[] Senders { get; init; } = [];

    /// <summary>Whether an answer is signed with the answerer's name unless they choose otherwise, so a component can preselect it.</summary>
    public bool SignWithName { get; init; }

    /// <summary>The name of that mailbox as support's configuration has it.</summary>
    public string Mailbox { get; init; }

    /// <summary>What this environment's outbound mail policy does with it.</summary>
    public MailPolicyEffect Effect { get; init; }

    /// <summary>The address it goes to instead, when <see cref="Effect"/> is <see cref="MailPolicyEffect.Redirect"/>.</summary>
    public string RedirectedTo { get; init; }

    /// <summary>
    /// For a case with no mail thread whose author the host can write to: the route the answer would take if it were mailed, so a component can offer
    /// <i>Answer by mail</i> and show where it would go from. Null when there is nothing to offer, which is every case that already has a mail thread.
    /// </summary>
    public SupportAnswerRoute MailOffer { get; init; }
}

/// <summary>What an environment's outbound mail policy does with a mail to one address.</summary>
public enum MailPolicyEffect
{
    /// <summary>Sent to the address as it is, as in production.</summary>
    None,

    /// <summary>Sent to the environment's override address instead.</summary>
    Redirect,

    /// <summary>Not sent at all.</summary>
    Withhold
}
