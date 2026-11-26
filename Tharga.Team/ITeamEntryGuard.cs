using System.Security.Claims;

namespace Tharga.Team;

/// <summary>
/// The host's rule for who may work in a team, and what to do about someone who may not yet: allow, deny with a reason, or send them to do
/// something first, such as a stronger sign-in.
/// </summary>
/// <remarks>
/// <b>Asked where the team's claims are built</b>, which is the same place for the HTTP request, the Blazor circuit's periodic revalidation and a
/// team switch, so the answer is enforced on the server and not only in the selector: the selected team travels in a cookie any browser can
/// set by hand. A refused team is simply not issued, so the caller has no team selected rather than one they may not use.
/// <para>
/// <b>It is asked only after the caller is known to be allowed in</b> (a member, or consented access), so it never decides who is in a team, only
/// whether someone who is may enter now. It is for interactive sessions; API keys and MCP stay governed by the team context middleware, and a host
/// that wants the same rule there can call its guard from its own code.
/// </para>
/// <para>
/// <b>The answer is cached for a short time</b> per user, session and team (<c>TeamEntryOptions.CacheDuration</c>), so keep the check itself cheap and
/// tell the library when an answer changes: <see cref="ITeamEntryGuardState.InvalidateAsync"/>. A guard that throws is handled as
/// <c>TeamEntryOptions.OnError</c> says, deny by default, and every refusal is audited with its reason.
/// </para>
/// Register it as a scoped service. With none registered, nothing changes.
/// </remarks>
public interface ITeamEntryGuard
{
    /// <summary>Whether <paramref name="user"/> may work in the team now.</summary>
    /// <param name="user">The caller, as their claims stand; read what the sign-in proved (<c>amr</c>, <c>auth_time</c>, ...) from it.</param>
    /// <param name="teamKey">The team they are entering.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    Task<TeamEntryDecision> CheckAsync(ClaimsPrincipal user, string teamKey, CancellationToken cancellationToken = default);
}

/// <summary>What <see cref="ITeamEntryGuard"/> says about one entry.</summary>
public sealed record TeamEntryDecision
{
    /// <summary>Which it is.</summary>
    public required TeamEntryOutcome Outcome { get; init; }

    /// <summary>Why entry is refused, in words the caller can read; shown by the team selector and recorded in the audit log. Null for an allow.</summary>
    public string Reason { get; init; }

    /// <summary>
    /// For <see cref="TeamEntryOutcome.StepUp"/>: where to send the caller. A <c>{returnUrl}</c> in it is replaced with the page they were on,
    /// URL-encoded, so the host's step-up page can send them back; without one nothing is added.
    /// </summary>
    /// <remarks>
    /// The return value is a path on this site that the library made from the current page, never anything a visitor typed. The host's step-up
    /// endpoint must still treat it as untrusted input when it redirects back: allow local paths only.
    /// </remarks>
    public string StepUpUrl { get; init; }

    /// <summary>Whether entry is allowed.</summary>
    public bool Allowed => Outcome == TeamEntryOutcome.Allow;

    /// <summary>Entry is allowed.</summary>
    public static TeamEntryDecision Allow { get; } = new() { Outcome = TeamEntryOutcome.Allow };

    /// <summary>Entry is refused, for <paramref name="reason"/>.</summary>
    public static TeamEntryDecision Deny(string reason) => new() { Outcome = TeamEntryOutcome.Deny, Reason = reason };

    /// <summary>Entry is refused until the caller has been to <paramref name="url"/> (see <see cref="StepUpUrl"/>), and <paramref name="reason"/> says why.</summary>
    public static TeamEntryDecision StepUp(string url, string reason = null) => new() { Outcome = TeamEntryOutcome.StepUp, StepUpUrl = url, Reason = reason };
}

/// <summary>How <see cref="ITeamEntryGuard"/> answered.</summary>
public enum TeamEntryOutcome
{
    /// <summary>The caller may work in the team.</summary>
    Allow,

    /// <summary>The caller may not, and nothing they can do here changes that.</summary>
    Deny,

    /// <summary>The caller may not yet: they are sent to do something first, then come back.</summary>
    StepUp
}

/// <summary>Lets the host tell the library that the guard's answer for a user changed, so the change does not wait for the cache or the next revalidation.</summary>
/// <remarks>
/// <b>Narrow on purpose</b>: it forgets what was decided and asks the guard again, and every live circuit of that user on this instance re-evaluates at
/// once. It does not issue or remove anything itself. Call it once, on the instance where the answer changed: it is announced through
/// <see cref="ITeamEntryNotifier"/>, and on several instances a host that relays that notifier reaches every other instance's circuits too
/// (Tharga/Team#471). Without a relay another instance catches up when its cached answer expires.
/// </remarks>
public interface ITeamEntryGuardState
{
    /// <summary>Forgets the answers for <paramref name="userKey"/>, in <paramref name="teamKey"/> only, or in every team when null, and has their circuits re-evaluate now.</summary>
    Task InvalidateAsync(string userKey, string teamKey = null, CancellationToken cancellationToken = default);
}
