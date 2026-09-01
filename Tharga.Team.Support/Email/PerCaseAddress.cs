namespace Tharga.Team.Support.Email;

/// <summary>
/// The per-case reply address, <c>support+{caseId}@example.com</c>, built and read back.
/// </summary>
/// <remarks>
/// <b>Both directions live together on purpose.</b> They are one convention, and a build that disagrees with
/// its parse produces replies that arrive and match nothing — a failure that looks like the mailbox not being
/// read.
/// </remarks>
internal static class PerCaseAddress
{
    /// <summary>
    /// Builds the reply address for a case, or null when the sending address cannot carry one.
    /// </summary>
    public static string Build(string fromAddress, string caseId)
    {
        if (string.IsNullOrWhiteSpace(fromAddress) || string.IsNullOrWhiteSpace(caseId)) return null;

        var (local, domain) = Split(fromAddress);

        if (local == null) return null;

        // A tag containing the separator could not be read back, and a case id is generated rather than
        // chosen, so refusing is better than emitting an address that will not parse.
        return caseId.Contains('+') || caseId.Contains('@') ? null : $"{local}+{caseId}@{domain}";
    }

    /// <summary>
    /// Finds the case id in whichever recipient is a per-case address of <paramref name="fromAddress"/>, or
    /// null when none is.
    /// </summary>
    public static string CaseIdIn(IEnumerable<string> recipients, string fromAddress)
    {
        if (recipients == null || string.IsNullOrWhiteSpace(fromAddress)) return null;

        var (local, domain) = Split(fromAddress);

        if (local == null) return null;

        foreach (var recipient in recipients)
        {
            var caseId = CaseIdOf(recipient, local, domain);

            if (caseId != null) return caseId;
        }

        return null;
    }

    private static string CaseIdOf(string recipient, string local, string domain)
    {
        if (string.IsNullOrWhiteSpace(recipient)) return null;

        var (recipientLocal, recipientDomain) = Split(recipient);

        if (recipientLocal == null || !string.Equals(recipientDomain, domain, StringComparison.OrdinalIgnoreCase))
            return null;

        var plus = recipientLocal.IndexOf('+');

        if (plus <= 0 || plus == recipientLocal.Length - 1) return null;

        return string.Equals(recipientLocal[..plus], local, StringComparison.OrdinalIgnoreCase)
            ? recipientLocal[(plus + 1)..]
            : null;
    }

    private static (string Local, string Domain) Split(string address)
    {
        var at = address.Trim().LastIndexOf('@');

        return at <= 0 || at == address.Trim().Length - 1
            ? (null, null)
            : (address.Trim()[..at], address.Trim()[(at + 1)..]);
    }
}
