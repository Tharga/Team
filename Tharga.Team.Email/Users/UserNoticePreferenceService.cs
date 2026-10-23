using System.Diagnostics;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Users;

/// <summary>The checked surface over notice choices: a user reads and changes their own, and a caller holding <see cref="SystemUserScopes.Manage"/> anyone's.</summary>
/// <remarks>
/// A user is offered the notices they may receive in at least one team they are an active member of. A choice is
/// global; a part of it is stored only where some default in force for them differs from it, so a later change to a
/// team's or the catalogue's default still reaches someone who chose what was the default anyway.
/// </remarks>
internal sealed class UserNoticePreferenceService(
    UserNoticeRegistry notices,
    UserMailCategoryRegistry categories,
    UserMailPreferences mailPreferences,
    IUserNoticePreferenceStore store,
    IUserMessageMemberDirectory members,
    UserNoticeEligibility eligibility,
    TeamAuthorizer authorizer,
    IUserService userService,
    CompositeAuditLogger auditLogger = null,
    IAuditEntryFactory auditEntryFactory = null) : IUserNoticePreferenceService
{
    public async Task<UserNoticeChoice[]> GetChoicesAsync(string userKey, CancellationToken cancellationToken = default)
    {
        await RequireSelfOrManageAsync(userKey, nameof(GetChoicesAsync));

        var context = await ContextAsync(userKey, cancellationToken);
        var choices = await store.GetChoicesAsync(userKey, cancellationToken) ?? new Dictionary<string, UserNoticeSetting>();

        var result = new List<UserNoticeChoice>();
        foreach (var notice in notices.All)
        {
            var teams = await EligibleTeamDefaultsAsync(notice, context, cancellationToken);
            if (teams == null) continue;

            result.Add(Choice(notice, choices.GetValueOrDefault(notice.Key), teams, context));
        }

        return [.. result];
    }

    public async Task<UserNoticeChoice> SetChoiceAsync(string userKey, string noticeKey, bool enabled, UserNoticeDelivery delivery, CancellationToken cancellationToken = default)
    {
        var notice = notices.Get(noticeKey, nameof(noticeKey));
        UserNoticeSettings.RequireAllowed(notice, enabled, delivery);

        try
        {
            await RequireSelfOrManageAsync(userKey, nameof(SetChoiceAsync));
        }
        catch (UnauthorizedAccessException e)
        {
            Audit(userKey, notice.Key, enabled, delivery, 0, false, e.Message);
            throw;
        }

        var sw = Stopwatch.StartNew();
        var context = await ContextAsync(userKey, cancellationToken);
        var teams = await EligibleTeamDefaultsAsync(notice, context, cancellationToken) ?? [];
        var stored = (await store.GetChoicesAsync(userKey, cancellationToken))?.GetValueOrDefault(notice.Key);

        var before = Choice(notice, stored, teams, context);
        var deviation = UserNoticeSettings.Deviation(notice, enabled, delivery, teams);
        await store.SetChoiceAsync(userKey, notice.Key, deviation, cancellationToken);
        var after = Choice(notice, deviation.IsEmpty ? null : deviation, teams, context);

        if (before.Enabled != after.Enabled || before.Delivery != after.Delivery || !Same(stored, deviation))
            Audit(userKey, notice.Key, after.Enabled, after.Delivery, sw.ElapsedMilliseconds, true, null);

        return after;
    }

    private async Task<UserContext> ContextAsync(string userKey, CancellationToken cancellationToken)
    {
        var memberships = await members.GetActiveMembershipsOfUserAsync(userKey, cancellationToken) ?? [];
        var mailChoices = await mailPreferences.GetChoicesAsync(userKey, cancellationToken);

        return new UserContext(memberships, mailChoices.ToDictionary(x => x.Category.Key, x => x.Receives, StringComparer.Ordinal), []);
    }

    /// <summary>The stored defaults of the teams the user may receive the notice in, or null when there is none.</summary>
    private async Task<UserNoticeSetting[]> EligibleTeamDefaultsAsync(UserNoticeType notice, UserContext context, CancellationToken cancellationToken)
    {
        var teams = new List<string>();
        foreach (var membership in context.Memberships)
        {
            if (!teams.Contains(membership.TeamKey, StringComparer.Ordinal) && await eligibility.MayReceiveAsync(notice, membership)) teams.Add(membership.TeamKey);
        }

        if (teams.Count == 0) return null;

        var defaults = new List<UserNoticeSetting>();
        foreach (var teamKey in teams)
        {
            if (!context.TeamDefaults.TryGetValue(teamKey, out var team))
            {
                team = await store.GetTeamDefaultsAsync(teamKey, cancellationToken) ?? new Dictionary<string, UserNoticeSetting>();
                context.TeamDefaults[teamKey] = team;
            }

            defaults.Add(team.GetValueOrDefault(notice.Key));
        }

        return [.. defaults];
    }

    private UserNoticeChoice Choice(UserNoticeType notice, UserNoticeSetting stored, UserNoticeSetting[] teams, UserContext context)
        => UserNoticeSettings.Choice(notice, categories.Find(notice.Category), stored, teams, context.CategoryReceives.GetValueOrDefault(notice.Category, true));

    private static bool Same(UserNoticeSetting stored, UserNoticeSetting deviation)
        => (stored == null || stored.IsEmpty) ? deviation.IsEmpty : stored.Enabled == deviation.Enabled && stored.Delivery == deviation.Delivery;

    private async Task RequireSelfOrManageAsync(string userKey, string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userKey);

        var current = await userService.GetCurrentUserAsync();
        if (current != null && string.Equals(current.Key, userKey, StringComparison.Ordinal)) return;

        if (await authorizer.HasSystemScopeAsync(SystemUserScopes.Manage)) return;

        throw new UnauthorizedAccessException($"{operation} on another user's notice choices requires the '{SystemUserScopes.Manage}' system scope.");
    }

    private void Audit(string userKey, string noticeKey, bool enabled, UserNoticeDelivery delivery, long durationMs, bool success, string error)
    {
        if (auditLogger == null || auditEntryFactory == null) return;

        auditLogger.Log(auditEntryFactory.Create(AuditEventType.DataChange, UserNoticeAudit.Feature, UserNoticeAudit.ChoiceAction, nameof(SetChoiceAsync), durationMs, success, error,
            metadata: new Dictionary<string, string>
            {
                [UserNoticeAudit.UserKey] = userKey,
                [UserNoticeAudit.Notice] = noticeKey,
                [UserNoticeAudit.Enabled] = enabled.ToString(),
                [UserNoticeAudit.Delivery] = delivery.ToString()
            }));
    }

    private sealed record UserContext(
        UserMessageMember[] Memberships,
        Dictionary<string, bool> CategoryReceives,
        Dictionary<string, IReadOnlyDictionary<string, UserNoticeSetting>> TeamDefaults);
}
