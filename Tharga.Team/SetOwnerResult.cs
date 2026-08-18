namespace Tharga.Team;

/// <summary>
/// What a set-owner did: whether it changed anything, and which owners it displaced.
/// </summary>
/// <param name="Changed">
/// False only when the requested owner was already the sole owner. Nothing was written and nothing was
/// audited.
/// </param>
/// <param name="DemotedOwnerKeys">
/// User keys of the owners demoted to <see cref="AccessLevel.Administrator"/>. Empty when the team had no
/// owner to displace — which is a real change, not a no-op.
/// </param>
/// <remarks>
/// <b>Two fields rather than one, because an empty list means two different things.</b> Repairing an
/// ownerless team demotes nobody, and so does doing nothing at all. Returning only the list would make the
/// audit decorator unable to tell a genuine repair from a sync pass that found the state already correct —
/// and it would silently stop recording repairs, which are the entries most worth having.
/// </remarks>
public sealed record SetOwnerResult(bool Changed, string[] DemotedOwnerKeys)
{
    /// <summary>The requested owner already held the role alone.</summary>
    public static SetOwnerResult NoChange { get; } = new(false, []);
}
