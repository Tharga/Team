namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>SupportInReplyTo</c> — the mail the application sent that a support case's first mail answered.
/// </summary>
public static class SupportInReplyToText
{
    /// <summary>Names the mail above the first message.</summary>
    public static readonly TextKey InReplyTo = new("team.support.inReplyTo.label", "In reply to:");

    /// <summary>Introduces when the mail was sent, for example "sent 2 Nov 09:00".</summary>
    public static readonly TextKey Sent = new("team.support.inReplyTo.sent", "sent");

    /// <summary>Introduces who the mail went to.</summary>
    public static readonly TextKey To = new("team.support.inReplyTo.to", "to");

    /// <summary>Opens the text of the mail.</summary>
    public static readonly TextKey Show = new("team.support.inReplyTo.show", "Show mail");

    /// <summary>Closes the text of the mail.</summary>
    public static readonly TextKey Hide = new("team.support.inReplyTo.hide", "Hide mail");

    /// <summary>Shown when the mail has expired from the mail log, rather than showing nothing.</summary>
    public static readonly TextKey NoLongerKept = new("team.support.inReplyTo.gone", "A mail the application sent, which is no longer kept.");

    /// <summary>Shown to a reader who may not read the mail log: the case is a reply, but the mail is not theirs to read.</summary>
    public static readonly TextKey NotPermitted = new("team.support.inReplyTo.notPermitted", "In reply to a mail the application sent. You cannot read it.");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [InReplyTo, Sent, To, Show, Hide, NoLongerKept, NotPermitted];
}
