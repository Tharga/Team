using Tharga.Team;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Which access levels each selector offers, and how a host's <c>HiddenAccessLevels</c> narrows them.
/// </summary>
/// <remarks>
/// Pure and static so the rules are testable without rendering anything — the same reason
/// <see cref="TeamOwnership"/> is. Before this existed the sets were four separate inline filters, which is
/// how they drifted apart in the first place.
/// <para>
/// <b>Hiding is subtractive, never an allow-list.</b> The selectors differ on purpose:
/// <see cref="ApiKey"/> keeps <see cref="AccessLevel.Custom"/> because least-privilege machine keys are what
/// that surface is for, while the member selectors exclude it. A host-supplied allow-list applied to all of
/// them would flatten that distinction; hiding layers over each surface's own rule and leaves it intact.
/// </para>
/// <para>
/// <b>Hidden is not invalid.</b> Nothing here touches what the model accepts, what a claim resolves to, or
/// what a badge renders. A host syncing members from another system can still receive a hidden level, and
/// those members keep working — this governs what a person can <i>choose</i>, and nothing else.
/// </para>
/// </remarks>
public static class AccessLevelVisibility
{
    /// <summary>
    /// Levels offered when inviting a member or editing one. <see cref="AccessLevel.Owner"/> is absent
    /// because ownership moves through its own operations, and <see cref="AccessLevel.Custom"/> because a
    /// member holding no base scopes at all is a machine-key shape, not a person.
    /// </summary>
    public static readonly AccessLevel[] Member =
        [AccessLevel.Administrator, AccessLevel.User, AccessLevel.Viewer];

    /// <summary>
    /// Levels offered for an API key. <b>Includes <see cref="AccessLevel.Custom"/> deliberately</b> — a key
    /// carrying only its explicit grants is the least-privilege case this surface exists to support.
    /// </summary>
    public static readonly AccessLevel[] ApiKey =
        [AccessLevel.Administrator, AccessLevel.User, AccessLevel.Viewer, AccessLevel.Custom];

    /// <summary>
    /// Levels a team can consent to expose itself at. Ascending, unlike the others, because the picker reads
    /// as "how much do we expose".
    /// </summary>
    public static readonly AccessLevel[] Consent =
        [AccessLevel.Viewer, AccessLevel.User, AccessLevel.Administrator];

    /// <summary>
    /// The levels <paramref name="offered"/> minus anything the host hid. Order is preserved.
    /// </summary>
    public static AccessLevel[] Apply(IReadOnlyList<AccessLevel> offered, IEnumerable<AccessLevel> hidden)
    {
        if (offered == null) return [];
        if (hidden == null) return [.. offered];

        var hiddenSet = hidden.ToHashSet();

        return [.. offered.Where(level => !hiddenSet.Contains(level))];
    }

    /// <summary>
    /// Rejects a <c>HiddenAccessLevels</c> configuration that cannot mean what it says. Called during
    /// registration, so a host learns at startup rather than from an empty dropdown.
    /// </summary>
    /// <remarks>
    /// Two refusals, and both exist because the alternative is a setting that silently does nothing or
    /// something broken.
    /// <list type="bullet">
    ///   <item><description>
    ///     <b><see cref="AccessLevel.Owner"/>.</b> No selector offers it, so hiding it changes nothing.
    ///     Accepting that teaches a host the setting worked — and somebody writing it to mean "nobody may
    ///     become Owner" holds a security misunderstanding worth correcting immediately, since ownership is
    ///     governed by the ownership operations rather than by a picker.
    ///   </description></item>
    ///   <item><description>
    ///     <b>Emptying a selector.</b> An invite dialog with nothing to pick is broken, not configured.
    ///   </description></item>
    /// </list>
    /// <para>
    /// <b><see cref="AccessLevel.Administrator"/> is deliberately allowed</b>, though it is the one worth
    /// thinking about: the Owner still manages the team, but nobody else can be given management without
    /// being handed ownership. Note the domain still produces Administrators either way —
    /// <c>TransferOwnershipAsync</c> and <c>SetOwnerAsync</c> both demote a displaced owner to that level —
    /// and those members keep working, which is the hidden-is-not-invalid rule behaving correctly.
    /// </para>
    /// </remarks>
    public static void Validate(IReadOnlyCollection<AccessLevel> hidden)
    {
        if (hidden == null || hidden.Count == 0) return;

        if (hidden.Contains(AccessLevel.Owner))
            throw new InvalidOperationException(
                $"{nameof(AccessLevel.Owner)} cannot be hidden because no selector offers it: ownership is " +
                "granted by TransferOwnershipAsync and SetOwnerAsync, never by choosing a level. Remove it " +
                "from HiddenAccessLevels — leaving it there would read as a restriction that is not in force.");

        Require(Member, hidden, "inviting and editing team members");
        Require(ApiKey, hidden, "creating API keys");
        Require(Consent, hidden, "setting team consent");
    }

    private static void Require(IReadOnlyList<AccessLevel> offered, IEnumerable<AccessLevel> hidden, string surface)
    {
        if (Apply(offered, hidden).Length > 0) return;

        throw new InvalidOperationException(
            $"HiddenAccessLevels hides every level offered for {surface}, leaving nothing to choose. " +
            $"That surface offers {string.Join(", ", offered)}.");
    }
}
