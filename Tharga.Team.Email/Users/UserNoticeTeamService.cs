using System.Diagnostics;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Users;

/// <summary>A team's notice defaults, reached only through the scope proxy, which refuses a caller without <see cref="NoticeScopes.Manage"/> on the team named.</summary>
/// <remarks>A part of a default is stored only where it differs from the catalogue's, and a change is audited against the team.</remarks>
internal sealed class UserNoticeTeamService(
    UserNoticeRegistry notices,
    IUserNoticePreferenceStore store,
    CompositeAuditLogger auditLogger = null,
    IAuditEntryFactory auditEntryFactory = null) : IUserNoticeTeamService
{
    public async Task<UserNoticeTeamDefault[]> GetTeamDefaultsAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var stored = await store.GetTeamDefaultsAsync(teamKey, cancellationToken) ?? new Dictionary<string, UserNoticeSetting>();

        return [.. notices.All.Select(x => Default(x, stored.GetValueOrDefault(x.Key)))];
    }

    public async Task<UserNoticeTeamDefault> SetTeamDefaultAsync(string teamKey, string noticeKey, bool enabled, UserNoticeDelivery delivery, CancellationToken cancellationToken = default)
    {
        var notice = notices.Get(noticeKey, nameof(noticeKey));
        if (!notice.Allows(delivery)) throw new ArgumentException($"The notice '{notice.Key}' cannot be delivered {delivery}.", nameof(delivery));

        var sw = Stopwatch.StartNew();
        var before = Default(notice, (await store.GetTeamDefaultsAsync(teamKey, cancellationToken))?.GetValueOrDefault(notice.Key));
        var deviation = UserNoticeSettings.TeamDeviation(notice, enabled, delivery);

        await store.SetTeamDefaultAsync(teamKey, notice.Key, deviation, cancellationToken);

        var after = Default(notice, deviation);
        if (before.Enabled != after.Enabled || before.Delivery != after.Delivery) Audit(teamKey, after, sw.ElapsedMilliseconds);

        return after;
    }

    private static UserNoticeTeamDefault Default(UserNoticeType notice, UserNoticeSetting stored)
    {
        var (enabled, delivery) = UserNoticeSettings.TeamDefault(notice, stored);

        return new UserNoticeTeamDefault { Notice = notice, Enabled = enabled, Delivery = delivery, IsSet = enabled != notice.OnByDefault || delivery != notice.DefaultDelivery };
    }

    private void Audit(string teamKey, UserNoticeTeamDefault value, long durationMs)
    {
        if (auditLogger == null || auditEntryFactory == null) return;

        auditLogger.Log(auditEntryFactory.Create(AuditEventType.DataChange, UserNoticeAudit.Feature, UserNoticeAudit.DefaultAction, nameof(SetTeamDefaultAsync), durationMs, true, null, teamKey,
            new Dictionary<string, string>
            {
                [UserNoticeAudit.Notice] = value.Notice.Key,
                [UserNoticeAudit.Enabled] = value.Enabled.ToString(),
                [UserNoticeAudit.Delivery] = value.Delivery.ToString()
            }));
    }
}
