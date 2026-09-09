namespace Tharga.Team;

/// <summary>
/// Invitation policy — how long an invitation stays acceptable.
/// </summary>
/// <remarks>
/// <b>In the core package rather than the Blazor one, for the reason <see cref="ConsentOptions"/> gives:</b>
/// this decides whether a caller may join a team, which is authorization rather than presentation, and every
/// surface that answers it has to agree. Resolve it as <c>IOptions&lt;InvitationOptions&gt;</c>; a host that
/// never configures it gets these defaults.
/// </remarks>
public class InvitationOptions
{
    /// <summary>
    /// How long an invitation remains acceptable, measured from when it was created. <c>null</c> — the
    /// default — means invitations never expire.
    /// </summary>
    /// <remarks>
    /// <b>Null by default on purpose.</b> Invitations did not expire before this option existed, and a
    /// lifetime applied on upgrade would silently invalidate every invitation already outstanding — including
    /// links people had been sent and not yet opened. Opting in is a decision a host makes knowingly.
    /// <para>
    /// This is the <i>default</i> for new invitations, not a rule applied to existing ones: an invitation
    /// carrying its own <see cref="Invitation.ExpiresAt"/> keeps that, which is what makes extending one
    /// possible without reissuing its code.
    /// </para>
    /// </remarks>
    public TimeSpan? Lifetime { get; set; }

    /// <summary>
    /// How many failed resolves a source may make inside <see cref="ThrottleWindow"/> before failures start
    /// being delayed. Default: 5. Zero or less turns throttling off.
    /// </summary>
    /// <remarks>
    /// <b>Generous rather than off by default.</b> A real invitee fails once or twice — a mistyped link, a
    /// code already accepted — and never reaches five, so the default costs nothing a person would notice
    /// while still slowing a script. Off by default would mean nobody gets it.
    /// </remarks>
    public int ThrottleFailureThreshold { get; set; } = 5;

    /// <summary>How long failures are remembered for. Default: five minutes.</summary>
    public TimeSpan ThrottleWindow { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The longest a failed resolve is delayed once the threshold is passed. Default: two seconds.
    /// </summary>
    /// <remarks>
    /// <b>A delay, never a refusal.</b> An invitee retrying a link, or several people in one office behind
    /// one address accepting invitations the same morning, must not be locked out — so the throttle slows a
    /// guess and makes it visible, and never turns a real invitation away.
    /// </remarks>
    public TimeSpan MaxThrottleDelay { get; set; } = TimeSpan.FromSeconds(2);
}
