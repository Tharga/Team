using MimeKit;

namespace Tharga.Team.Email.Users;

/// <summary>The one way a host's list becomes addresses: each distinct address once, in the order first listed, with whether it will be mailed; the check before sending and the send itself both use it.</summary>
/// <remarks>
/// An address is compared trimmed and in lower case. It is mailed when any row naming it has no reason of the host's to
/// leave it out, and it has not opted out of the category or of all contact mail; its name and detail are the first given
/// by such a row, and its references every such row's. An address of which every row is left out keeps every row's
/// references and the first reason, and one that is not a mail address is left out whatever the rows say.
/// </remarks>
internal static class ContactAudience
{
    /// <summary>The distinct addresses of <paramref name="addressees"/> to look opt-outs up for: those that are mail addresses and that some row does not leave out.</summary>
    public static string[] Candidates(IEnumerable<ContactAddressee> addressees)
        => [.. Group(addressees).Where(x => x.Valid && x.Rows.Any(Included)).Select(x => x.Key)];

    /// <summary>Each distinct address and whether it will be mailed in <paramref name="categoryKey"/>, given the opt-outs recorded for the addresses.</summary>
    public static ContactAddresseeCheck[] Check(IEnumerable<ContactAddressee> addressees, string categoryKey, IEnumerable<ContactMailSuppression> suppressions)
    {
        var stopping = (suppressions ?? [])
            .Where(x => x != null && x.AppliesTo(categoryKey))
            .GroupBy(x => x.Address, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Any(y => y.Category == null), StringComparer.Ordinal);

        return [.. Group(addressees).Select(group =>
        {
            var included = group.Rows.Where(Included).ToArray();
            var speaking = included.Length > 0 ? included : group.Rows;
            var check = new ContactAddresseeCheck
            {
                Address = group.Valid ? group.Key : group.Rows[0].Address?.Trim() ?? string.Empty,
                Name = First(speaking.Select(x => x.Name)),
                Detail = First(speaking.Select(x => x.Detail)),
                References = [.. speaking.Select(x => x.Reference?.Trim()).Where(x => !string.IsNullOrEmpty(x)).Distinct(StringComparer.Ordinal)],
                Status = ContactAddresseeStatus.Reached
            };

            if (!group.Valid) return check with { Status = ContactAddresseeStatus.LeftOut, InvalidAddress = true };
            if (included.Length == 0) return check with { Status = ContactAddresseeStatus.LeftOut, ExcludedReason = group.Rows.Select(x => x.ExcludedReason?.Trim()).First(x => !string.IsNullOrEmpty(x)) };
            if (stopping.TryGetValue(group.Key, out var all)) return check with { Status = ContactAddresseeStatus.OptedOut, OptedOutOfAll = all };

            return check;
        })];
    }

    /// <summary>The checked addresses with the counts.</summary>
    public static ContactAddresseeList List(ContactAddresseeCheck[] checks) => new()
    {
        Items = checks,
        Reached = checks.Count(x => x.Status == ContactAddresseeStatus.Reached),
        OptedOut = checks.Count(x => x.Status == ContactAddresseeStatus.OptedOut),
        LeftOut = checks.Count(x => x.Status == ContactAddresseeStatus.LeftOut)
    };

    /// <summary>Whether a value is a mail address as it is stored: trimmed, lower case, one mailbox with a domain.</summary>
    public static bool IsAddress(string normalized)
        => normalized != null && normalized.Contains('@') && MailboxAddress.TryParse(normalized, out var mailbox) && mailbox.Address == normalized;

    private static bool Included(ContactAddressee row) => string.IsNullOrWhiteSpace(row.ExcludedReason);

    private static IEnumerable<(string Key, bool Valid, ContactAddressee[] Rows)> Group(IEnumerable<ContactAddressee> addressees)
        => (addressees ?? [])
            .Where(x => x != null)
            .GroupBy(x => UserMailAddresses.Normalize(x.Address) ?? string.Empty, StringComparer.Ordinal)
            .Select(x => (x.Key, IsAddress(x.Key), x.ToArray()));

    private static string First(IEnumerable<string> values) => values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim();
}
