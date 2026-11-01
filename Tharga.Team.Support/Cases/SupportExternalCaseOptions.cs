namespace Tharga.Team.Support.Cases;

/// <summary>
/// How cases opened from a public form through <see cref="IExternalSupportCaseService"/> behave.
/// </summary>
public class SupportExternalCaseOptions
{
    internal const string DefaultReceiptText = "Thank you for contacting us. We have received your message and will answer by mail. Reply to this mail to add to it.";

    /// <summary>
    /// Whether the visitor is mailed a receipt, through the primary support mailbox, when their case is opened.
    /// Default <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <b>Off by default, because a public form that mails whoever types an address into it</b> can be used to mail other
    /// people. Turn it on when the form is protected against that (a captcha, a rate limit). The receipt starts the mail
    /// thread, so the visitor's reply to it reaches the case. A receipt that cannot be sent is logged and never stops the
    /// case being opened.
    /// </remarks>
    public bool SendReceipt { get; set; }

    /// <summary>
    /// What the receipt says above the visitor's own message, which it quotes. Null uses an English default; set it for
    /// the language of your site.
    /// </summary>
    public string ReceiptText { get; set; }
}
