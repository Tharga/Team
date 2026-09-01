using Tharga.Team.Support.Email;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// How support cases reach the outside world.
/// </summary>
/// <remarks>
/// Separate from the notification options on purpose: a host may want Slack notifications without support
/// cases, or support cases without Slack. Neither should configure the other into existence.
/// <para>
/// <b>Why <see cref="Email"/> is a section while the Slack settings are flat.</b> Not taste, and not worth
/// tidying: <see cref="SlackChannel"/> and <see cref="SigningSecret"/> shipped at this level, and moving them
/// for symmetry would break the configuration of every host that has set them. A breaking change buys
/// nothing here. The reshape belongs in the release that is already breaking.
/// </para>
/// <para>
/// <b>Why mail is configured here and Slack's token is not.</b> The Slack transport is configured on
/// <c>AddThargaSupport</c> because notifications use it too. Nothing but support cases sends or reads mail,
/// so putting it there would force a host wanting email cases to register the notification module — a sink
/// and a hosted service it never asked for.
/// </para>
/// </remarks>
public class SupportCaseOptions
{
    /// <summary>
    /// Channel that support-case threads are posted to — a name (<c>#support</c>) or an id
    /// (<c>C0123456789</c>). Leave unset to keep cases entirely on the site.
    /// </summary>
    /// <remarks>
    /// <b>Unset is a supported configuration, not a misconfiguration.</b> A case with no channel is complete
    /// and trackable; the projection is optional. Nothing warns about this being empty.
    /// </remarks>
    public string SlackChannel { get; set; }

    /// <summary>
    /// Secret Slack signs inbound event requests with. Required only to receive replies from Slack.
    /// </summary>
    /// <remarks>
    /// Without it the inbound endpoint refuses everything, which is the correct failure: an endpoint that
    /// cannot verify a signature must not accept the request rather than trusting it.
    /// </remarks>
    public string SigningSecret { get; set; }

    /// <summary>
    /// Whether a component asks for a subject when a case is raised. Default <c>false</c>.
    /// </summary>
    /// <remarks>
    /// <b>A hint to the UI, not a rule the service enforces</b>, and the distinction is deliberate. With this
    /// off, a component shows no subject field and the message supplies one. With it on, the field appears —
    /// but a case raised without a subject anyway still gets a derived one, because
    /// <see cref="SupportCase.Subject"/> is not nullable and a half-filled form must not produce a case that
    /// renders as a blank line in every list.
    /// <para>
    /// So this decides what a person is asked for, never whether the case ends up with a subject.
    /// </para>
    /// </remarks>
    public bool UseSubject { get; set; }

    /// <summary>
    /// How long a case waits on the customer before it closes itself. Default 7 days;
    /// <see cref="TimeSpan.Zero"/> turns it off.
    /// </summary>
    /// <remarks>
    /// <b>The clock runs only while the case is waiting on the *customer*</b> — support answered and nobody
    /// came back. A case whose newest entry is the customer's is waiting on support, and closing that would
    /// hide the backlog rather than tidy it.
    /// <para>
    /// <b>Zero registers no background work at all</b>, rather than registering a sweep that finds nothing. A
    /// host that does not want cases closing itself runs nothing on its behalf.
    /// </para>
    /// </remarks>
    public TimeSpan AutoCloseAfter { get; set; } = TimeSpan.FromDays(7);

    /// <summary>How often the sweep runs. Default one hour.</summary>
    /// <remarks>
    /// It decides only how promptly a case closes after it becomes eligible, so an hour is generous for a
    /// span measured in days. Frequent sweeps cost a query and buy nothing.
    /// </remarks>
    public TimeSpan AutoCloseSweepInterval { get; set; } = TimeSpan.FromHours(1);

    /// <summary>Most cases one sweep will close, so a large backlog cannot become one enormous pass.</summary>
    public int AutoCloseBatchSize { get; set; } = 100;

    /// <summary>
    /// Reading and sending mail, and which recipients this instance answers for. Leave the hosts unset to
    /// keep email off.
    /// </summary>
    public MailOptions Email { get; } = new();
}
