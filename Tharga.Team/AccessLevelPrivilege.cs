

namespace Tharga.Team;

/// <summary>
/// Compares two access levels by privilege, which is not the same as comparing them by value.
/// </summary>
/// <remarks>
/// <b>The enum is ordered by privilege descending:</b> <c>Owner=0, Administrator=1, User=2, Viewer=3</c>.
/// So <i>less</i> privilege is a <i>larger</i> ordinal, and the instinctive <c>Math.Min</c> guard against
/// escalation does exactly the opposite of what it looks like it does.
/// <para>
/// <b><see cref="AccessLevel.Custom"/> is not a rank.</b> It sits at ordinal 4 but means "no base scopes
/// at all" — <c>ScopeRegistry.GetScopesForAccessLevel</c> returns empty for it and it is exempt from the
/// Owner/Administrator all-scopes rule. Treating it as rank 4 happens to give the right answer for the
/// floor and the wrong reason for it, so it is handled explicitly.
/// </para>
/// <para>
/// <b>Null — no level set — is the floor</b>, strictly below <see cref="AccessLevel.Custom"/>: it grants nothing
/// at all, where Custom still lets roles and overrides through. Before 4.0 an unset level was the zero value,
/// <see cref="AccessLevel.Owner"/>, the top of the ladder.
/// </para>
/// </remarks>
internal static class AccessLevelPrivilege
{
    /// <summary>
    /// Whether <paramref name="candidate"/> grants no more privilege than <paramref name="actual"/>.
    /// </summary>
    public static bool IsNoMorePrivilegedThan(AccessLevel? candidate, AccessLevel? actual)
        => candidate == actual || Rank(candidate) > Rank(actual);

    /// <summary>
    /// Position on the privilege ladder, least privileged highest.
    /// </summary>
    /// <remarks>
    /// <b>This mapping is deliberately not observable today, and that is a known limitation rather than
    /// an oversight.</b> <c>Custom</c> is ordinal 4 and every ranked level is 0–3, so a plain
    /// <c>(int)level</c> produces identical results for all five values — a mutation run replacing this
    /// body with the raw cast stays green, and no test can distinguish them without changing the enum.
    /// <para>
    /// It stays because the two agree by <i>accident</i>. <c>Custom</c> is the floor because it grants no
    /// base scopes, not because it happens to sort last; a level inserted after it, or a reordering,
    /// would silently invert the comparison while every test still passed. Writing the reason down as
    /// code costs one line and survives a reader who does not know the accident.
    /// </para>
    /// <para>
    /// The trip-wire is <c>AccessLevelPrivilegeTests.EveryAccessLevelIsAccountedFor</c>, which fails the
    /// moment the enum changes shape — so the assumption cannot drift unnoticed even though this line
    /// cannot be tested directly.
    /// </para>
    /// </remarks>
    private static int Rank(AccessLevel? level) => level switch
    {
        null => int.MaxValue,
        AccessLevel.Custom => int.MaxValue - 1,
        { } ranked => (int)ranked
    };

    /// <summary>
    /// The level a simulation may present: <paramref name="candidate"/> when it is a de-escalation, and
    /// otherwise <paramref name="actual"/> unchanged.
    /// </summary>
    /// <remarks>
    /// Falls back rather than throwing. A simulation that cannot be honoured should leave the caller at
    /// their real level and let the difference report say so — throwing would turn a stale cookie into a
    /// broken session.
    /// </remarks>
    public static AccessLevel Clamp(AccessLevel candidate, AccessLevel actual)
        => IsNoMorePrivilegedThan(candidate, actual) ? candidate : actual;

    /// <summary>
    /// <see cref="Clamp(AccessLevel, AccessLevel)"/> where either side may be unset. An unset
    /// <paramref name="actual"/> clamps everything to null: nothing can be presented above no access.
    /// </summary>
    public static AccessLevel? Clamp(AccessLevel? candidate, AccessLevel? actual)
        => IsNoMorePrivilegedThan(candidate, actual) ? candidate : actual;
}
