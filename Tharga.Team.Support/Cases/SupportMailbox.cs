using Tharga.Team.Email;

namespace Tharga.Team.Support.Cases;

/// <summary>One mail account support reads and answers from, under the name its cases store.</summary>
internal sealed record SupportMailbox(string Name, MailOptions Options)
{
    public bool IsPrimary => string.Equals(Name, SupportCaseOptions.PrimaryMailboxName, StringComparison.OrdinalIgnoreCase);

    public static SupportMailbox Primary(MailOptions options) => new(SupportCaseOptions.PrimaryMailboxName, options ?? new MailOptions());
}

/// <summary>Every support mailbox the host connected, the primary first.</summary>
internal sealed class SupportMailboxes
{
    public SupportMailboxes(MailOptions primary, IEnumerable<SupportMailbox> additional = null)
    {
        Primary = SupportMailbox.Primary(primary);
        All = [Primary, .. additional ?? []];
    }

    public SupportMailbox Primary { get; }

    public IReadOnlyList<SupportMailbox> All { get; }

    /// <summary>The mailbox a binding names, the primary for none, and null for one no longer configured.</summary>
    public SupportMailbox Find(string name)
        => string.IsNullOrWhiteSpace(name)
            ? Primary
            : All.FirstOrDefault(x => string.Equals(x.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a mailbox of this instance sends from the address.</summary>
    public bool IsOwnAddress(string address)
        => !string.IsNullOrWhiteSpace(address) && All.Any(x => SameAddress(x.Options.FromAddress, address));

    /// <summary>The strictest file limits of the mailboxes that send, so an answer that passes can go out through any of them.</summary>
    public SupportAttachmentLimits AnswerLimits()
    {
        var sending = All.Where(x => x.Options.Smtp.IsConfigured).Select(x => x.Options).ToArray();

        return sending.Length == 0 ? SupportAttachmentLimitCheck.LimitsOf(Primary.Options) : SupportAttachmentLimitCheck.Strictest(sending);
    }

    internal static bool SameAddress(string left, string right)
        => !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) && string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
