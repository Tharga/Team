using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Features.Team;

/// <summary>One line in the notification menu.</summary>
/// <param name="TeamKey">The team the line is about; choosing it opens that team.</param>
/// <param name="Text">What the line says.</param>
/// <param name="NeedsDecision">Whether it is a request the caller can decide, rather than their own waiting request.</param>
internal sealed record TeamNotificationItem(string TeamKey, string Text, bool NeedsDecision);

/// <summary>
/// What <c>TeamNotificationMenu</c> shows: the count on the bell, and the lines in the menu.
/// </summary>
/// <remarks>Pure and static so it is unit-testable.</remarks>
internal static class TeamNotificationMenuModel
{
    /// <summary>
    /// The number on the bell — requests waiting for the caller's decision. The caller's own waiting requests are listed
    /// but not counted: the count is for things the caller has to do.
    /// </summary>
    public static int Count(IReadOnlyCollection<TeamAccessRequestItem> awaitingMe) => awaitingMe?.Count ?? 0;

    /// <summary>Requests to decide first, newest first, then the caller's own pending requests.</summary>
    public static IReadOnlyList<TeamNotificationItem> Items(
        IEnumerable<TeamAccessRequestItem> awaitingMe, IEnumerable<TeamAccessRequestItem> mine, TextSet text)
    {
        var decide = (awaitingMe ?? [])
            .OrderByDescending(x => x.Request.RequestedAt)
            .Select(x => new TeamNotificationItem(x.TeamKey, text.Format(TeamAccessRequestText.AwaitingItem, x.TeamName,
                text.Format(TeamAccessRequestText.RequestLine, x.Request.RequesterName ?? x.Request.RequesterKey, x.Request.AccessLevel,
                    TeamAccessRequestGate.DurationPhrase(x.Request.Duration, text))), NeedsDecision: true));

        var waiting = (mine ?? [])
            .Where(x => x.Request.Status == TeamAccessRequestStatus.Pending)
            .OrderByDescending(x => x.Request.RequestedAt)
            .Select(x => new TeamNotificationItem(x.TeamKey, text.Format(TeamAccessRequestText.YourRequestFor, x.TeamName), NeedsDecision: false));

        return [.. decide, .. waiting];
    }
}
