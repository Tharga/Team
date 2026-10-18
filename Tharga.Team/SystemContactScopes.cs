namespace Tharga.Team;

/// <summary>System scope constants for mail the product's staff write to contacts: people who are not users, whose addresses the host holds in its own records.</summary>
/// <remarks>
/// <b>A system scope and no team scope</b>, as for messages to users: writing to people outside every team is a decision
/// about the whole product, never a team's.
/// </remarks>
public static class SystemContactScopes
{
    /// <summary>Write to contacts the host lists, see which of them opted out, read the log of every message sent to contacts, and opt an address out.</summary>
    public const string Send = "contacts:send";
}
