namespace Tharga.Team.Support.Cases;

/// <summary>How a support answer mail speaks for support: the name earlier support entries are attributed to, the signature, and the time zone dates are shown in.</summary>
/// <remarks>
/// <b>No staff member is named in an answer mail.</b> The answer itself carries no heading, and every earlier entry
/// written by support or by the assistant is attributed to <see cref="SupportName"/>; the customer's own entries keep
/// their name. The mail is framed in <c>MailTemplateOptions.SupportHtml</c> when the host has set one.
/// </remarks>
public class SupportAnswerOptions
{
    /// <summary>Who an earlier entry written by support is attributed to, such as <c>Example Support</c>; null uses the text key <c>team.support.mail.supportName</c>, "Support" in English.</summary>
    public string SupportName { get; set; }

    /// <summary>The lines of plain text signed after every answer, before the conversation it quotes; empty signs nothing.</summary>
    /// <remarks>In the HTML part the lines are one paragraph with the class <c>signature</c>, encoded and joined by line breaks, so a template can style it.</remarks>
    public string[] Signature { get; set; } = [];

    /// <summary>
    /// The role whose members have their answers sent from their own address and name, such as <c>Support</c>. Empty, the
    /// default, sends every answer from a support address.
    /// </summary>
    /// <remarks>
    /// <b>Needs the mailbox to say it may.</b> The address is used only when its domain is in <c>MailOptions.SendAsDomains</c>
    /// or it is one of the mailbox's <c>SendAs</c> addresses; otherwise the answer falls back to the address the customer wrote
    /// to, and then to the mailbox's own. Replies always go to a support address, with the case's tracker, never to the staff address.
    /// Mail nobody wrote, such as a receipt or the assistant's answer, never goes from a personal address.
    /// </remarks>
    public string StaffSenderRole { get; set; }

    /// <summary>Whether an answer is signed with the name of the staff member who wrote it by default. Default <c>false</c>; the one answering can change it per answer.</summary>
    public bool NameTheAnswerer { get; set; }

    /// <summary>
    /// The words that close an answer when it is signed with a name, such as <c>Vänliga hälsningar,{newline}{Name}</c>; null uses the text key
    /// <c>team.support.mail.closing</c>, <c>Kind regards,{newline}{Name}</c> in English. <c>{Name}</c> is the answerer's name and <c>{newline}</c> a line break.
    /// </summary>
    public string Closing { get; set; }

    /// <summary>
    /// How the conversation so far is quoted after an answer. <see cref="SupportQuoteStyle.Nested"/>, the default, quotes as Gmail does,
    /// one block with a bar at its left and older entries nested inside; <see cref="SupportQuoteStyle.PerEntry"/> keeps one block per entry.
    /// </summary>
    public SupportQuoteStyle QuoteStyle { get; set; } = SupportQuoteStyle.Nested;

    /// <summary>The time zone the dates of quoted entries are shown in, as a system time zone id such as <c>Europe/Stockholm</c>, default <c>UTC</c>.</summary>
    /// <remarks>A date in UTC says so; one in another zone is shown as that zone's local time alone.</remarks>
    public string TimeZoneId { get; set; } = "UTC";
}
