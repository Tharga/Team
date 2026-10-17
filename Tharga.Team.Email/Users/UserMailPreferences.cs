using System.Diagnostics;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Users;

/// <summary>Reads and changes users' mail choices with no caller check, for the checked service, the unsubscribe link and the sender to share.</summary>
/// <remarks>Only a choice that differs from the category's default is stored, and a change is audited only when it changes what the user receives.</remarks>
internal sealed class UserMailPreferences(
    UserMailCategoryRegistry categories,
    IUserMailPreferenceStore store,
    CompositeAuditLogger auditLogger = null,
    IAuditEntryFactory auditEntryFactory = null)
{
    public async Task<IReadOnlyList<UserMailChoice>> GetChoicesAsync(string userKey, CancellationToken cancellationToken)
    {
        var stored = await store.GetChoicesAsync(userKey, cancellationToken);

        return [.. categories.All.Select(x => ChoiceOf(x, stored))];
    }

    public async Task<bool> ReceivesAsync(string userKey, UserMailCategory category, CancellationToken cancellationToken)
    {
        if (category.Required) return true;

        var stored = await store.GetChoicesAsync(userKey, cancellationToken);

        return ChoiceOf(category, stored).Receives;
    }

    /// <exception cref="InvalidOperationException">The category is required and <paramref name="receives"/> is false.</exception>
    public async Task<UserMailChoice> SetAsync(string userKey, UserMailCategory category, bool receives, string action, CancellationToken cancellationToken)
    {
        if (category.Required)
        {
            if (!receives) throw new InvalidOperationException($"The mail category '{category.Key}' is required and cannot be turned off.");

            return new UserMailChoice { Category = category, Receives = true, IsDefault = true };
        }

        var sw = Stopwatch.StartNew();
        var before = ChoiceOf(category, await store.GetChoicesAsync(userKey, cancellationToken));

        if (receives == category.OnByDefault) await store.ClearChoiceAsync(userKey, category.Key, cancellationToken);
        else await store.SetChoiceAsync(userKey, category.Key, receives, cancellationToken);

        if (before.Receives != receives) Audit(action, userKey, category.Key, receives, sw.ElapsedMilliseconds, true, null);

        return new UserMailChoice { Category = category, Receives = receives, IsDefault = receives == category.OnByDefault };
    }

    public void AuditRefusal(string action, string userKey, string categoryKey, bool receives, string reason)
        => Audit(action, userKey, categoryKey, receives, 0, false, reason);

    private void Audit(string action, string userKey, string categoryKey, bool receives, long durationMs, bool success, string error)
    {
        if (auditLogger == null || auditEntryFactory == null) return;

        auditLogger.Log(auditEntryFactory.Create(AuditEventType.DataChange, UserMailAudit.Feature, action, action == UserMailAudit.UnsubscribeAction ? "Unsubscribe" : nameof(IUserMailPreferenceService.SetChoiceAsync), durationMs, success, error,
            metadata: new Dictionary<string, string>
            {
                [UserMailAudit.UserKey] = userKey,
                [UserMailAudit.Category] = categoryKey,
                [UserMailAudit.Receives] = receives.ToString()
            }));
    }

    private static UserMailChoice ChoiceOf(UserMailCategory category, IReadOnlyDictionary<string, bool> stored)
    {
        bool? choice = !category.Required && stored != null && stored.TryGetValue(category.Key, out var value) ? value : null;

        return new UserMailChoice { Category = category, Receives = category.Receives(choice), IsDefault = choice == null || choice == category.OnByDefault };
    }
}
