using System.Diagnostics;
using System.Globalization;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Users;

/// <summary>The checked surface over a user's mail history: a user reads their own, and a caller holding <see cref="SystemUserScopes.Manage"/> anyone's.</summary>
/// <remarks>
/// A user's own read is not audited; staff reading someone else's is, and so is a refusal, each naming the user and
/// never an address. The user's current address is read from their record, so mail queued to that address alone,
/// such as the invitation that brought them in, is part of their history.
/// </remarks>
internal sealed class UserMailHistoryService(
    IUserMailQueue queue,
    IUserMailRecipientDirectory recipients,
    TeamAuthorizer authorizer,
    IUserService userService,
    CompositeAuditLogger auditLogger = null,
    IAuditEntryFactory auditEntryFactory = null) : IUserMailHistoryService
{
    private const int MaxPageSize = 100;

    public async Task<UserMailHistoryPage> GetMineAsync(string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var current = await userService.GetCurrentUserAsync();
        if (string.IsNullOrWhiteSpace(current?.Key)) return new UserMailHistoryPage();

        return await ReadAsync(current.Key, cursor, pageSize, cancellationToken);
    }

    public async Task<UserMailHistoryPage> GetForUserAsync(string userKey, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userKey);

        var key = userKey.Trim();
        var current = await userService.GetCurrentUserAsync();
        if (current != null && string.Equals(current.Key, key, StringComparison.Ordinal)) return await ReadAsync(key, cursor, pageSize, cancellationToken);

        var sw = Stopwatch.StartNew();

        if (!await authorizer.HasSystemScopeAsync(SystemUserScopes.Manage))
        {
            var refusal = $"{nameof(GetForUserAsync)} on another user's mail history requires the '{SystemUserScopes.Manage}' system scope.";
            Audit(key, 0, sw.ElapsedMilliseconds, false, refusal);
            throw new UnauthorizedAccessException(refusal);
        }

        var page = await ReadAsync(key, cursor, pageSize, cancellationToken);
        Audit(key, page.Items.Length, sw.ElapsedMilliseconds, true, null);

        return page;
    }

    private async Task<UserMailHistoryPage> ReadAsync(string userKey, string cursor, int pageSize, CancellationToken cancellationToken)
    {
        var address = (await recipients.FindAsync(userKey, cancellationToken))?.EMail;
        var page = await queue.ListForRecipientAsync(userKey, address, cursor, Math.Clamp(pageSize, 1, MaxPageSize), cancellationToken) ?? new UserMailDeliveryPage();

        return new UserMailHistoryPage
        {
            Items = [.. page.Items.Select(x => new UserMailHistoryItem
            {
                Id = x.Id,
                Kind = x.Kind,
                Category = x.Category,
                Subject = x.Subject,
                Status = x.Status,
                Reason = x.Reason,
                Address = x.RecipientAddress,
                QueuedAt = x.QueuedAt,
                ChangedAt = x.ChangedAt
            })],
            NextCursor = page.NextCursor
        };
    }

    private void Audit(string userKey, int count, long durationMs, bool success, string error)
    {
        if (auditLogger == null || auditEntryFactory == null) return;

        auditLogger.Log(auditEntryFactory.Create(AuditEventType.ServiceCall, UserMailAudit.Feature, UserMailAudit.HistoryAction, nameof(IUserMailHistoryService.GetForUserAsync), durationMs, success, error,
            metadata: new Dictionary<string, string>
            {
                [UserMailAudit.UserKey] = userKey,
                [UserMailAudit.Count] = count.ToString(CultureInfo.InvariantCulture)
            }));
    }
}
