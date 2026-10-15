namespace Tharga.Team;

/// <summary>
/// Whether a team's mail key is the one in use or the one being phased out.
/// </summary>
public enum MailKeyRole
{
    /// <summary>The key used for everything new.</summary>
    Primary,

    /// <summary>A former primary, still accepted until it retires.</summary>
    Secondary
}
