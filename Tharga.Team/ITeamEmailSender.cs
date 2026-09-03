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
/// <b>They are not unified, and not given a fallback either.</b> An invitation is usually sent from
/// <c>noreply@</c>, while support mail must come from an address replies return to — inheriting one for the
/// other would send support mail from a no-reply address and lose every reply. A host wanting one mailbox
/// for both binds them from the same configuration section, explicitly.
/// </para>
/// <para>
/// When nothing is registered, the invite dialogs fall back to manual link copying rather than failing. That
/// is a supported configuration, not a degraded one — but it looks identical to having forgotten to configure
/// email, so a host seeing invitations go unsent should check whether a sender is registered at all.
/// </para>
/// </remarks>
public interface ITeamEmailSender
{
    Task SendInviteAsync(string recipientEmail, string recipientName, string inviteLink, string teamName);
}
