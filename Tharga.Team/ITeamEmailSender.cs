namespace Tharga.Team;

/// <summary>
/// Sends the invitation email. Implement it to use your own mail infrastructure (SendGrid, Azure, an existing
/// pipeline) instead of the built-in SMTP sender.
/// </summary>
/// <remarks>
/// <b>Invitations are the only mail the core sends</b>, and this interface is the whole surface for them.
/// Support cases are the exception, and a deliberate one: <c>Tharga.Team.Support</c> reads and sends mail
/// through its own transport, its own options and its own SMTP stack.
/// <para>
/// <b>With mail to users registered</b> (<c>AddThargaUserMail</c> in <c>Tharga.Team.Email</c>), invitations are
/// sent through its outbox instead, in the required account category and from its address, so each is recorded,
/// shows in the invited person's mail history and is marked bounced when it comes back. A sender the host
/// registered itself is left in place.
/// </para>
/// <para>
/// <b>Not given a fallback to support's settings.</b> Each sends from the address it is configured with, and every
/// one of them should be an address a reply reaches: a no-reply address loses every reply. A host wanting one
/// mailbox for both binds them from the same configuration section, explicitly.
/// </para>
/// <para>
/// When nothing is registered, the invite dialogs fall back to manual link copying rather than failing. That
/// is a supported configuration, not a degraded one — but it looks identical to having forgotten to configure
/// email, so a host seeing invitations go unsent should check whether a sender is registered at all.
/// </para>
/// </remarks>
public interface ITeamEmailSender
{
    /// <summary>
    /// Sends one invitation. <see cref="TeamInviteMail.TeamKey"/> identifies the team, so mail that depends on it —
    /// its language, branding or sender — looks the team up by key rather than by display name or ambient state.
    /// </summary>
    /// <remarks>
    /// The only member since 4.0. The four-string overload it replaced (recipient email, recipient name, link and
    /// team name) was removed; a sender that implemented it moves the same four values off <paramref name="mail"/>.
    /// </remarks>
    Task SendInviteAsync(TeamInviteMail mail);
}
