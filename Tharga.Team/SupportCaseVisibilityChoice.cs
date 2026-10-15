namespace Tharga.Team;

/// <summary>What the caller may choose about who reads a case: whether they may change it, and whether only they may read it.</summary>
public sealed record SupportCaseVisibilityChoice(bool MayChange, bool MayBePrivate)
{
    /// <summary>Nothing to choose: the host keeps cases team-wide, or the caller did not raise this one.</summary>
    public static SupportCaseVisibilityChoice None { get; } = new(false, false);
}
