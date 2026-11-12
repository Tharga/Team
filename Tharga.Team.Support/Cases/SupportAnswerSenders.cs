using Tharga.Team.Email;

namespace Tharga.Team.Support.Cases;

/// <summary>The staff member answering: the address and name an answer may go out under, when they are allowed to.</summary>
internal sealed record SupportStaffSender(string Address, string Name);

/// <summary>One address an answer can be sent from, and the mailbox whose account sends it.</summary>
internal sealed record SupportSenderOption(SupportMailbox Mailbox, string Address, string Name, bool Personal)
{
    /// <summary>The sender as <c>Name &lt;address&gt;</c>, or the bare address when there is no name.</summary>
    public string Formatted => SupportAnswerSenders.Format(Address, Name);
}

/// <summary>
/// Decides which addresses an answer may be sent from and which one it goes from unless the answerer picks another.
/// </summary>
/// <remarks>
/// <b>Declared by the host, never guessed.</b> A mailbox sends as its own address, as each address in its
/// <see cref="MailOptions.SendAs"/>, and as a staff member's own address only when that address is a send-as address or
/// on one of its <see cref="MailOptions.SendAsDomains"/>.
/// <para>
/// <b>The default, in order:</b> the staff member's own address; the address the customer wrote to, when it is one the
/// mailbox may send as; the mailbox's own address.
/// </para>
/// </remarks>
internal static class SupportAnswerSenders
{
    /// <summary>Every address the answerer may send from: the case's mailbox first, then the others, then the staff member's own when a mailbox covers it.</summary>
    public static IReadOnlyList<SupportSenderOption> Allowed(IReadOnlyList<SupportMailbox> mailboxes, SupportMailbox caseMailbox, SupportStaffSender staff)
    {
        var ordered = new[] { caseMailbox }.Concat(mailboxes.Where(x => !ReferenceEquals(x, caseMailbox))).ToArray();
        var options = new List<SupportSenderOption>();

        foreach (var mailbox in ordered)
        {
            Add(options, new SupportSenderOption(mailbox, Trimmed(mailbox.Options.FromAddress), Named(mailbox.Options.FromName), false));

            foreach (var sendAs in mailbox.Options.SendAs ?? [])
                Add(options, new SupportSenderOption(mailbox, Trimmed(sendAs?.Address), Named(sendAs?.Name) ?? Named(mailbox.Options.FromName), false));
        }

        if (staff != null && Trimmed(staff.Address) is { } own && options.All(x => !MailboxSame(x.Address, own)))
        {
            var owner = ordered.FirstOrDefault(x => Covers(x.Options, own));
            if (owner != null) options.Add(new SupportSenderOption(owner, own, Named(staff.Name), true));
        }

        return options;
    }

    /// <summary>
    /// The address of the mailbox a mail was delivered to: the first delivery address that is the mailbox's own or one it may send as,
    /// and the mailbox's own address when none is; null when the mailbox has no address.
    /// </summary>
    public static string DeliveredAddress(MailOptions options, IEnumerable<string> deliveredTo)
    {
        var own = Trimmed(options.FromAddress);
        var addresses = new[] { own }.Concat((options.SendAs ?? []).Select(x => Trimmed(x?.Address))).Where(x => x != null).ToArray();

        return (deliveredTo ?? []).Select(x => addresses.FirstOrDefault(a => MailboxSame(a, x))).FirstOrDefault(x => x != null) ?? own;
    }

    /// <summary>The address an answer goes from unless another is chosen.</summary>
    public static SupportSenderOption Default(IReadOnlyList<SupportSenderOption> allowed, SupportMailbox caseMailbox, string deliveredTo)
        => allowed.FirstOrDefault(x => x.Personal)
           ?? allowed.FirstOrDefault(x => MailboxSame(x.Address, deliveredTo))
           ?? allowed.FirstOrDefault(x => ReferenceEquals(x.Mailbox, caseMailbox))
           ?? allowed.FirstOrDefault();

    /// <summary>The option for an address the answerer chose, or the default for none; null for an address that is not allowed.</summary>
    public static SupportSenderOption Choose(IReadOnlyList<SupportSenderOption> allowed, SupportSenderOption fallback, string chosen)
        => string.IsNullOrWhiteSpace(chosen) ? fallback : allowed.FirstOrDefault(x => MailboxSame(x.Address, chosen));

    /// <summary>Whether the mailbox's account may send as the address: it is one of its send-as addresses, or on one of its send-as domains.</summary>
    public static bool Covers(MailOptions options, string address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        if ((options.SendAs ?? []).Any(x => MailboxSame(x?.Address, address))) return true;

        var at = address.LastIndexOf('@');
        if (at < 0) return false;

        var domain = address[(at + 1)..].Trim();

        return (options.SendAsDomains ?? []).Any(x => string.Equals((x ?? string.Empty).Trim().TrimStart('@'), domain, StringComparison.OrdinalIgnoreCase));
    }

    public static string Format(string address, string name)
        => string.IsNullOrWhiteSpace(name) ? address : new MimeKit.MailboxAddress(name, address).ToString();

    private static void Add(List<SupportSenderOption> options, SupportSenderOption option)
    {
        if (option.Address != null && options.All(x => !MailboxSame(x.Address, option.Address))) options.Add(option);
    }

    private static bool MailboxSame(string left, string right) => SupportMailboxes.SameAddress(left, right);

    private static string Trimmed(string address) => string.IsNullOrWhiteSpace(address) ? null : address.Trim();

    private static string Named(string name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim();
}
