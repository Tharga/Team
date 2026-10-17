namespace Tharga.Team;

/// <summary>System scope constants for messages written by the product's staff to users of its teams.</summary>
/// <remarks>
/// <b>A system scope and no team scope, deliberately.</b> A team never writes mail to its users by hand; only staff do,
/// across whichever teams they choose, so the grant is a decision about the whole product.
/// </remarks>
public static class SystemMessageScopes
{
    /// <summary>Write a message to chosen users of any team, see who a selection reaches, and read the log of every message sent.</summary>
    public const string Send = "messages:send";
}
