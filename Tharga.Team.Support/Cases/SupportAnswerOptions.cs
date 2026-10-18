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

    /// <summary>The time zone the dates of quoted entries are shown in, as a system time zone id such as <c>Europe/Stockholm</c>, default <c>UTC</c>.</summary>
    /// <remarks>A date in UTC says so; one in another zone is shown as that zone's local time alone.</remarks>
    public string TimeZoneId { get; set; } = "UTC";
}
