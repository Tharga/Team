namespace Tharga.Team.Email.Mailbox;

/// <summary>Whether the catch-all mailbox can be read: not configured, reachable, or failing with a reason.</summary>
public sealed record MailboxHealth(bool IsConfigured, bool IsReachable, string Error = null)
{
    public static MailboxHealth NotConfigured { get; } = new(false, false);
}
