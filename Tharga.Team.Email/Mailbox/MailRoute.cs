namespace Tharga.Team.Email.Mailbox;

/// <summary>
/// Decides where a received mail goes, from the addresses it was delivered to.
/// </summary>
/// <remarks>
/// <b>The envelope, never the <c>To</c> header.</b> A forward or a BCC does not show in <c>To</c>; the delivery
/// headers say which of our addresses the mail actually reached. They arrive most trustworthy first, and the
/// first one that is ours decides.
/// <para>
/// <b>Nothing is dropped.</b> A mail no current key claims — a retired key, a mistyped one, an address that is
/// not ours at all — goes to the postmaster, because a mail that reached an organisation's address may already
/// count as received by it.
/// </para>
/// </remarks>
internal static class MailRoute
{
    public static async Task<MailRouteDecision> DecideAsync(IReadOnlyList<string> deliveredTo, TeamMailboxOptions options, Func<string, Task<TeamMailKeyRecord>> findKey, DateTime now)
    {
        var mailbox = options.Mail?.Imap?.UserName;
        foreach (var address in deliveredTo.Where(x => !IsTheCatchAll(x, mailbox)))
        {
            var parsed = TeamMailAddress.Parse(address, options);
            if (parsed == null) continue;

            var key = await findKey(parsed.Key);
            if (key == null) return Postmaster(parsed.ItemKey, address, MailRouteReason.UnknownAddress);
            if (key.HasRetired(now)) return Postmaster(parsed.ItemKey, address, MailRouteReason.RetiredKey);

            return key.Role == MailKeyRole.Primary
                ? new MailRouteDecision(key.TeamKey, parsed.ItemKey, address, MailRouteReason.PrimaryKey, null)
                : new MailRouteDecision(key.TeamKey, parsed.ItemKey, address, MailRouteReason.RetiringKey, key.RetiresAt);
        }

        return Postmaster(null, Intended(deliveredTo, options), MailRouteReason.UnknownAddress);
    }

    // A catch-all names its own mailbox first, so the address the sender wrote is the first one on our domain that is not that mailbox.
    private static string Intended(IReadOnlyList<string> deliveredTo, TeamMailboxOptions options)
    {
        var mailbox = options.Mail?.Imap?.UserName;
        var suffix = "@" + options.Domain;
        return deliveredTo.FirstOrDefault(x => x.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && !IsTheCatchAll(x, mailbox))
               ?? deliveredTo.FirstOrDefault();
    }

    // With no prefix or suffix the catch-all's own local part reads like a key, so it is never read as a team address.
    private static bool IsTheCatchAll(string address, string mailbox) =>
        !string.IsNullOrWhiteSpace(mailbox) && string.Equals(address?.Trim().Trim('<', '>').Trim(), mailbox.Trim(), StringComparison.OrdinalIgnoreCase);

    private static MailRouteDecision Postmaster(string itemKey, string deliveredTo, MailRouteReason reason) => new(null, itemKey, deliveredTo, reason, null);
}
