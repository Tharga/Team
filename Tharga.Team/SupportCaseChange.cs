namespace Tharga.Team;

/// <summary>What happened to a support case.</summary>
public enum SupportCaseChange
{
    /// <summary>The case was created.</summary>
    Raised,

    /// <summary>An entry was added to its transcript.</summary>
    Replied,

    /// <summary>The case was closed.</summary>
    Closed
}
