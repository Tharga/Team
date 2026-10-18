namespace Tharga.Team;

/// <summary>
/// System scope constants for the postmaster: mail that reached the shared mailbox but belongs to no team.
/// </summary>
/// <remarks>
/// <b>Never team scopes.</b> Unrouted mail went to an address nobody owns any more, or never did — a mistyped
/// key, a retired one — so it may hold any team's material. Holding <see cref="MailboxScopes.Read"/> on one
/// team must not reveal it.
/// </remarks>
public static class SystemMailboxScopes
{
    /// <summary>List unrouted mail and open it and its files; every open is audited.</summary>
    public const string PostmasterRead = "mailbox:postmaster:read";

    /// <summary>Route an unrouted mail to a team, or archive it without routing and restore it.</summary>
    public const string PostmasterManage = "mailbox:postmaster:manage";
}
