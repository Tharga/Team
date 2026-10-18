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

    /// <summary>The address it is sent from, the mailbox the case came in through.</summary>
    public string From { get; init; }

    /// <summary>The name of that mailbox as support's configuration has it.</summary>
    public string Mailbox { get; init; }

    /// <summary>What this environment's outbound mail policy does with it.</summary>
    public MailPolicyEffect Effect { get; init; }

    /// <summary>The address it goes to instead, when <see cref="Effect"/> is <see cref="MailPolicyEffect.Redirect"/>.</summary>
    public string RedirectedTo { get; init; }
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
