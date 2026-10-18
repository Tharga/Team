namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>MailPreviewDialog</c> and the Preview buttons that open it, in the message compose view and the support answer boxes.</summary>
public static class MailPreviewText
{
    /// <summary>The button that opens the preview.</summary>
    public static readonly TextKey Open = new("team.mailPreview.open", "Preview");

    /// <summary>The dialog's title.</summary>
    public static readonly TextKey Title = new("team.mailPreview.title", "What the recipient receives");

    /// <summary>Names the sender line.</summary>
    public static readonly TextKey From = new("team.mailPreview.from", "From");

    /// <summary>Names the reply address line.</summary>
    public static readonly TextKey ReplyTo = new("team.mailPreview.replyTo", "Reply to");

    /// <summary>Names the recipient line.</summary>
    public static readonly TextKey To = new("team.mailPreview.to", "To");

    /// <summary>The recipient line of a mail to many; <c>{0}</c> is how many.</summary>
    public static readonly TextKey ToMany = new("team.mailPreview.toMany", "Each of the {0} people, by name");

    /// <summary>Names the subject line.</summary>
    public static readonly TextKey Subject = new("team.mailPreview.subject", "Subject");

    /// <summary>Names the files that go with the mail.</summary>
    public static readonly TextKey Files = new("team.mailPreview.files", "Files");

    /// <summary>Above the body of a mail sent as text alone.</summary>
    public static readonly TextKey TextOnly = new("team.mailPreview.textOnly", "Sent as text alone.");

    /// <summary>The accessible name of the frame the mail is shown in.</summary>
    public static readonly TextKey Frame = new("team.mailPreview.frame", "The mail as it will look");

    /// <summary>The button that closes the dialog.</summary>
    public static readonly TextKey Close = new("team.mailPreview.close", "Close");

    /// <summary>When the preview could not be made; <c>{0}</c> is why.</summary>
    public static readonly TextKey Failed = new("team.mailPreview.failed", "The preview could not be made. {0}");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Open, Title, From, ReplyTo, To, ToMany, Subject, Files, TextOnly, Frame, Close, Failed];
}
