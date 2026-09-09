using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service;

/// <summary>
/// Slows repeated failed invite-code resolves from one source, and records the first one that crosses the
/// threshold as <see cref="AuditEventType.RateLimit"/>.
/// </summary>
/// <remarks>
/// <b>Resolving is an oracle, and that is by design.</b> A hit returns the team's name because the screen
/// has to say which team you were invited to; a miss returns null. So a guess announces itself, and the pool
/// of live codes only grows, since invitations do not expire unless a host configures a lifetime
/// (Tharga/Team#256).
/// <para>
/// <b>This is a second layer and never a substitute for entropy.</b> Against a distributed source a
/// per-source delay barely moves a weak code, so the token length must never be reduced on the strength of
/// it. What it does buy is real: it slows a single source, and it turns an enumeration attempt from
/// something invisible into an audit entry.
/// </para>
/// <para>
/// <b>Delay, never refusal.</b> A legitimate invitee retrying a link, or an office of people behind one
/// address accepting invitations the same morning, must not be turned away — so a throttled resolve still
/// answers, just later.
/// </para>
/// </remarks>
internal sealed class ThrottledTeamInvitationService(
    ITeamInvitationService inner,
    InvitationThrottle throttle,
    IOptions<InvitationOptions> options = null,
    IHttpContextAccessor httpContextAccessor = null,
    IAuditLogger auditLogger = null)
    : ITeamInvitationService
{
    /// <summary>
    /// What a caller with no determinable address is counted under.
    /// </summary>
    /// <remarks>
    /// <b>A Blazor circuit has no client address to read.</b> <see cref="IHttpContextAccessor"/> answers
    /// during a request — including the server-side render that precedes an interactive circuit — and not
    /// afterwards, and nothing in the toolkit captures one at connection time. Everything unattributable
    /// therefore shares a bucket, so a burst still slows down; the cost is that unrelated callers share it
    /// during an attack, which is affordable precisely because the consequence is a delay rather than a
    /// refusal.
    /// </remarks>
    private const string UnknownSource = "(unattributed)";

    public async Task<TeamInvitation> GetInvitationAsync(string inviteCode)
    {
        var invitation = await inner.GetInvitationAsync(inviteCode);
        if (invitation != null) return invitation;

        // Tolerating an absent options registration rather than requiring one: a decorator that throws
        // where a host simply never configured the feature is the defect class this release already fixed
        // twice. The defaults are the documented behaviour anyway.
        var (delay, justTripped) = throttle.RecordFailure(Source(), options?.Value ?? new InvitationOptions());

        if (justTripped) Audit();
        if (delay > TimeSpan.Zero) await Task.Delay(delay);

        return null;
    }

    private string Source()
        => httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? UnknownSource;

    /// <summary>
    /// <see cref="AuditEventType.RateLimit"/> was declared, filtered and rendered by the log view, and
    /// raised by nothing until now.
    /// </summary>
    /// <remarks>
    /// The entry deliberately carries no invite code. A code that failed is one somebody guessed, and
    /// writing guesses into the audit log would put candidate codes in front of everyone who can read it.
    /// </remarks>
    private void Audit()
    {
        if (auditLogger == null) return;

        auditLogger.Log(AuditHelper.BuildEntry(
            httpContextAccessor,
            feature: "invitation",
            action: "throttle",
            methodName: nameof(GetInvitationAsync),
            durationMs: 0,
            success: false,
            errorMessage: "Repeated failed invitation resolves from one source; further failures are delayed.",
            eventType: AuditEventType.RateLimit));
    }
}
