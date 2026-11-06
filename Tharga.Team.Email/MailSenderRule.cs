namespace Tharga.Team.Email;

/// <summary>What a mailbox does with mail from a sender a <see cref="MailSenderRule"/> names.</summary>
public enum MailSenderAction
{
    /// <summary>
    /// No case and no event. The mail is left untouched in the mailbox, as everything the handler ignores is.
    /// </summary>
    Ignore = 0,

    /// <summary>
    /// The case is stored already closed, for a host that wants a record. It raises no
    /// <c>SupportCaseChange.Raised</c> event either: it was never open, and a host announcing on that event must
    /// stay silent for it.
    /// </summary>
    Close = 1
}

/// <summary>
/// Senders whose mail a mailbox handles by an action the host chooses, rather than as a customer asking for help
/// (Tharga/Team#449).
/// </summary>
/// <remarks>
/// <b>For automated mail that carries no automated-mail marker</b> and is addressed to the support address itself,
/// so neither the automated check nor the recipient filter can tell it apart: a DMARC aggregate report sent to a
/// <c>rua=mailto:</c> address is the common case, one mail a day.
/// <para>
/// A sender is an address (<c>noreply-dmarc-support@google.com</c>) or every address at a domain
/// (<c>*@google.com</c>). Matching ignores case and is made on the <c>From</c> address as the handler reads it.
/// <b>A <c>From</c> header authenticates nobody</b>, so a rule is a filter on what to do with mail that names a
/// sender, not a statement that the sender is who it says. Anyone can send as an address on the list and have the
/// mail dropped; that is the cost of the rule, and it only ever removes a case, never adds access.
/// </para>
/// </remarks>
public sealed class MailSenderRule
{
    /// <summary>Addresses, or <c>*@domain</c> for every address at a domain.</summary>
    public string[] Senders { get; set; } = [];

    /// <summary>What happens to mail from them. <see cref="MailSenderAction.Ignore"/> unless set.</summary>
    public MailSenderAction Action { get; set; }

    /// <summary>Whether <paramref name="from"/> is one of <see cref="Senders"/>.</summary>
    public bool Matches(string from)
    {
        var address = Normalize(from);
        if (address.Length == 0) return false;

        var at = address.LastIndexOf('@');
        if (at <= 0 || at == address.Length - 1) return false;

        foreach (var sender in (Senders ?? []).Select(Normalize).Where(x => x.Length > 0))
        {
            if (sender == address) return true;
            if (sender.StartsWith("*@", StringComparison.Ordinal) && sender[2..] == address[(at + 1)..]) return true;
        }

        return false;
    }

    /// <summary>Takes the address out of a <c>"Name" &lt;addr&gt;</c> value and lower-cases it.</summary>
    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var text = value.Trim();
        var open = text.LastIndexOf('<');
        var close = text.LastIndexOf('>');
        if (open >= 0 && close > open) text = text[(open + 1)..close];

        return text.Trim().ToLowerInvariant();
    }
}
