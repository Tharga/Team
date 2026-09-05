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
}
