namespace Tharga.Team.Email;

/// <summary>The host's HTML frame around every HTML mail the toolkit sends: support answers, and mail to users — messages, notices and invitations.</summary>
/// <remarks>
/// <b>Trusted markup.</b> The template is the host's own and is used as it is written; only what is placed into it is
/// sanitized. <see cref="ContentPlaceholder"/> is required; the others are optional. With no template, every mail is sent
/// exactly as it was before templates existed.
/// </remarks>
public class MailTemplateOptions
{
    /// <summary>Replaced by the mail's own HTML; a mail that has only text gets it escaped into paragraphs. Required.</summary>
    public const string ContentPlaceholder = "{{content}}";

    /// <summary>Replaced by the subject line, escaped.</summary>
    public const string SubjectPlaceholder = "{{subject}}";

    /// <summary>Replaced by the footer — why the mail came and how to stop it, for an optional category — or by nothing; without this placeholder the footer follows the content.</summary>
    public const string FooterPlaceholder = "{{footer}}";

    /// <summary>Replaced by the start of the mail's text on one line, escaped, for the preview line an inbox shows after the subject.</summary>
    public const string PreheaderPlaceholder = "{{preheader}}";

    /// <summary>The template, an HTML document or fragment holding <see cref="ContentPlaceholder"/>; null or empty sends each mail's own HTML unframed.</summary>
    public string Html { get; set; }
}
