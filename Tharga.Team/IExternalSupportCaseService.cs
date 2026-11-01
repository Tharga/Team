namespace Tharga.Team.Support.Cases;

/// <summary>
/// Opens a support case for somebody who is not signed in, from a public contact form.
/// </summary>
/// <remarks>
/// <b>This is anonymous on purpose, and the only anonymous support entry point.</b> Every call on
/// <see cref="ISupportCaseService"/> needs a signed-in user and a team, and none of them is loosened for this. A
/// separate interface means the unauthenticated door is one named method rather than an exception inside an
/// authorizing decorator.
/// <para>
/// <b>The case opens unassigned and stays out of every team.</b> The address is unverified, so it is used to suggest
/// teams to staff, as a mailed case does, and never to place the case. Staff assign it.
/// </para>
/// <para>
/// <b>Answers reach the visitor by mail</b>, through the mailbox support cases are configured with. Without a mailbox
/// that sends, the case is still opened, so a visitor's message is never lost, but nothing can be mailed back.
/// </para>
/// <para>
/// <b>Abuse is shared between this and the host.</b> The toolkit bounds every field and the message and refuses what
/// is malformed. Rate limiting, a honeypot field and a captcha are the host's: only the host knows who is calling.
/// </para>
/// </remarks>
public interface IExternalSupportCaseService
{
    /// <summary>Opens an unassigned case from a visitor's message.</summary>
    /// <param name="contact">Who wrote; the address is required.</param>
    /// <param name="subject">The subject, or <c>null</c> to derive one from the message.</param>
    /// <param name="body">What the visitor wrote.</param>
    /// <exception cref="ArgumentException">The address is missing or malformed, the message is empty, or something is longer than <see cref="SupportCaseLimits"/> allows.</exception>
    Task<SupportCase> RaiseExternalCaseAsync(ExternalSupportContact contact, string subject, string body, CancellationToken cancellationToken = default);
}
