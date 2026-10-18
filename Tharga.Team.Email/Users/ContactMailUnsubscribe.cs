using System.Diagnostics;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Users;

/// <summary>Opts an address out of one contact category, or of all contact mail, on the authority of an unsubscribe link, with no sign-in.</summary>
/// <remarks>
/// <b>The token is the check.</b> It names the address and the category and cannot be altered: whoever holds it received
/// the mail at that address, so it authorizes that address's own opt-out and nothing else. A new opt-out is audited, never
/// with the address.
/// </remarks>
internal sealed class ContactMailUnsubscribe(
    UserMailUnsubscribeToken tokens,
    ContactMailCategoryRegistry categories,
    IContactMailSuppressionStore store,
    TimeProvider timeProvider,
    CompositeAuditLogger auditLogger = null,
    IAuditEntryFactory auditEntryFactory = null)
{
    /// <summary>The address and the category the link names, or null when the link is not valid.</summary>
    public ContactMailUnsubscribeLink Read(string token)
    {
        if (!tokens.TryReadAddress(token, out var address, out var categoryKey)) return null;

        var category = categories.Find(categoryKey);

        return category == null ? null : new ContactMailUnsubscribeLink(address, category);
    }

    /// <summary>Opts the link's address out of its category, or with <paramref name="everyCategory"/> of all contact mail, and returns the link; null when the link is not valid. Asking again changes nothing.</summary>
    public async Task<ContactMailUnsubscribeLink> UnsubscribeAsync(string token, bool everyCategory, ContactMailSuppressionSource source, CancellationToken cancellationToken)
    {
        var link = Read(token);
        if (link == null) return null;

        var sw = Stopwatch.StartNew();
        var added = await store.AddAsync(new ContactMailSuppression
        {
            Address = link.Address,
            Category = everyCategory ? null : link.Category.Key,
            SuppressedAt = timeProvider.GetUtcNow().UtcDateTime,
            Source = source
        }, cancellationToken);

        if (added && auditLogger != null && auditEntryFactory != null)
        {
            auditLogger.Log(auditEntryFactory.Create(AuditEventType.DataChange, ContactMessageAudit.Feature, ContactMessageAudit.UnsubscribeAction, "Unsubscribe", sw.ElapsedMilliseconds, true, null,
                metadata: new Dictionary<string, string>
                {
                    [ContactMessageAudit.Category] = everyCategory ? ContactMessageAudit.EveryCategory : link.Category.Key,
                    [ContactMessageAudit.Source] = source.ToString()
                }));
        }

        return link;
    }
}

/// <summary>What a contact's unsubscribe link names: one address and one contact category.</summary>
internal sealed record ContactMailUnsubscribeLink(string Address, ContactMailCategory Category);
