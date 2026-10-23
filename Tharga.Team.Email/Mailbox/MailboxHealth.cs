namespace Tharga.Team.Email.Mailbox;

/// <summary>Whether a mailbox's server can be reached: not configured, reachable, or failing with a reason.</summary>
public sealed record MailboxHealth(bool IsConfigured, bool IsReachable, string Error = null)
{
    public static MailboxHealth NotConfigured { get; } = new(false, false);
}
