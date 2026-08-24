namespace Tharga.Team.Support.Cases;

/// <summary>
/// How support cases reach the outside world.
/// </summary>
/// <remarks>
/// Separate from the notification options on purpose: a host may want Slack notifications without support
/// cases, or support cases without Slack. Neither should configure the other into existence.
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
}
