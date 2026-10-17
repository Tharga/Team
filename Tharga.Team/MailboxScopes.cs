namespace Tharga.Team;

/// <summary>
/// Team scope constants for a team's mailbox.
/// </summary>
/// <remarks>
/// <b>Three, because three different people use a mailbox.</b> Reading mail, deciding what becomes of a mail
/// and changing the team's address are separate jobs: a case officer handles mail every day and should never
/// rotate the address every sender knows, and an administrator who rotates it need not read anyone's mail.
/// </remarks>
public static class MailboxScopes
{
    /// <summary>List and open the team's mail, and see the team's addresses.</summary>
    public const string Read = "mailbox:read";

    /// <summary>Record what became of a mail — handled, and by what.</summary>
    public const string Handle = "mailbox:handle";

    /// <summary>Issue and rotate the team's address.</summary>
    public const string Manage = "mailbox:manage";
}
