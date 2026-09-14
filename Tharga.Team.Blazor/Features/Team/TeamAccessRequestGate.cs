using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Features.Team;

/// <summary>A duration a requester can pick.</summary>
/// <param name="Key">Stable identifier for the picker.</param>
/// <param name="Label">What the picker shows.</param>
/// <param name="Duration">The duration, or null for no end.</param>
internal sealed record AccessRequestDurationChoice(string Key, TextKey Label, TimeSpan? Duration);

/// <summary>
/// What the access-request UI offers and how it phrases it.
/// </summary>
/// <remarks>
/// Pure and static so it is unit-testable; the project has no bUnit, so a decision left in markup cannot be tested. The
/// service enforces every rule here — these decide only what is offered.
/// </remarks>
internal static class TeamAccessRequestGate
{
    /// <summary>The durations offered, shortest first, ending with no end.</summary>
    public static IReadOnlyList<AccessRequestDurationChoice> DurationChoices { get; } =
    [
        new("1h", TeamAccessRequestText.ChoiceOneHour, TimeSpan.FromHours(1)),
        new("8h", TeamAccessRequestText.ChoiceEightHours, TimeSpan.FromHours(8)),
        new("1d", TeamAccessRequestText.ChoiceOneDay, TimeSpan.FromDays(1)),
        new("7d", TeamAccessRequestText.ChoiceOneWeek, TimeSpan.FromDays(7)),
        new("30d", TeamAccessRequestText.ChoiceThirtyDays, TimeSpan.FromDays(30)),
        new("none", TeamAccessRequestText.ChoiceNoEnd, null)
    ];

    /// <summary>
    /// Whether the caller can decide requests on <paramref name="teamKey"/>: <c>team:manage</c> for the selected team, as
    /// a member. Scope claims are issued for the selected team only, so another team's requests cannot be decided from here.
    /// </summary>
    public static bool CanDecide(bool hasManageScope, string selectedTeamKey, string teamKey, bool isMember)
        => isMember && TeamActionGate.CanManage(hasManageScope, selectedTeamKey, teamKey);

    /// <summary>Whether the caller may ask for access: not a member, and holding a consent role.</summary>
    public static bool CanRequest(bool isMember, IEnumerable<string> callerRoles, IEnumerable<string> consentRoles)
    {
        if (isMember) return false;

        var offered = new HashSet<string>(consentRoles ?? [], StringComparer.Ordinal);
        return (callerRoles ?? []).Any(offered.Contains);
    }

    /// <summary>The caller's pending request on <paramref name="teamKey"/>, if any.</summary>
    public static TeamAccessRequest PendingOn(IEnumerable<TeamAccessRequestItem> mine, string teamKey)
        => (mine ?? [])
            .Where(x => x.TeamKey == teamKey && x.Request.Status == TeamAccessRequestStatus.Pending)
            .Select(x => x.Request)
            .OrderByDescending(x => x.RequestedAt)
            .FirstOrDefault();

    /// <summary>"for 8 hours", "for 2 days", "with no end" — a duration as it reads inside a sentence.</summary>
    public static string DurationPhrase(TimeSpan? duration, TextSet text)
    {
        if (duration is not { } length) return text[TeamAccessRequestText.WithNoEnd];

        if (length.TotalDays >= 1 && length.TotalDays % 1 == 0)
            return length.TotalDays == 1 ? text[TeamAccessRequestText.ForOneDay] : text.Format(TeamAccessRequestText.ForDays, (int)length.TotalDays);

        var hours = Math.Max(1, (int)Math.Round(length.TotalHours));
        return hours == 1 ? text[TeamAccessRequestText.ForOneHour] : text.Format(TeamAccessRequestText.ForHours, hours);
    }

    /// <summary>
    /// What approving grants, said plainly: every holder of the consent roles, not only the requester.
    /// </summary>
    public static string ApprovalWarning(IEnumerable<string> consentRoles, TeamAccessRequest request, string teamName, TextSet text)
        => text.Format(TeamAccessRequestText.ApproveWarning,
            string.Join(", ", consentRoles ?? []), request.AccessLevel, teamName, DurationPhrase(request.Duration, text));

    /// <summary>
    /// The notice for a temporary consent — what it grants, until when, and what it returns to — or null while consent is
    /// standing.
    /// </summary>
    public static string TemporaryConsentNotice(ITeam team, AccessLevel defaultLevel, DateTime utcNow, TextSet text)
    {
        var inForce = TeamConsent.Resolve(team, utcNow);
        if (inForce.ExpiresAt is not { } expiresAt) return null;

        var previous = team.TemporaryConsent;
        var returnsTo = previous?.PreviousConsentedRoles is { Length: > 0 }
            ? (previous.PreviousConsentAccessLevel ?? defaultLevel).ToString()
            : text[TeamAccessRequestText.NoAccess];

        return text.Format(TeamAccessRequestText.TemporaryConsent,
            inForce.AccessLevel ?? defaultLevel, expiresAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), returnsTo);
    }
}
