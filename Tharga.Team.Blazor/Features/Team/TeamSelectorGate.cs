namespace Tharga.Team.Blazor.Features.Team;

/// <summary>
/// Decides what the team selector offers: a way to create a team when the caller belongs to none, and a
/// way to search when they belong to too many to scan.
/// </summary>
/// <remarks>
/// Pure and static so it is unit-testable — the project has no bUnit, so a decision left in razor markup
/// is unreachable from tests. Mirrors <c>TeamActionGate</c> / <c>TeamVisibility</c> / <c>MemberHighlight</c>.
/// </remarks>
internal static class TeamSelectorGate
{
    /// <summary>
    /// Whether the teamless branch offers a "Create team" affordance.
    /// </summary>
    /// <remarks>
    /// <c>AllowTeamCreation</c> is documented as hiding the Create and Delete buttons, and
    /// <c>CreateTeamPath</c> names the selector's teamless link as one of the two built-in entry points —
    /// so the option unambiguously governs this link. It was read by <c>TeamComponent</c> only, which left
    /// the two surfaces contradicting each other: the selector offered creation that the service layer
    /// then refused, because creating a team requires <c>AllowTeamCreation</c> at the service since 3.1.2.
    /// <para>
    /// Applies to <b>both</b> link variants — the host-callback branch and the plain navigation branch.
    /// Gating only one would leave the defect in place for whichever hosts use the other.
    /// </para>
    /// </remarks>
    /// <param name="teamCount">Teams the caller belongs to.</param>
    /// <param name="allowTeamCreation">The host's <c>AllowTeamCreation</c> option.</param>
    /// <param name="showLink">
    /// The selector's own <c>ShowCreateTeamLink</c> parameter — <b>presentation only</b>. It hides the
    /// link in the top bar without saying anything about whether teams may be created, which is what
    /// <paramref name="allowTeamCreation"/> governs and what the service enforces.
    /// </param>
    /// <remarks>
    /// Three inputs, and only one of them is a permission. A host that wants creation reachable from the
    /// team page but not offered in the top bar is making a layout decision, not an authorization one —
    /// so it gets its own switch rather than being folded into the option that the service also reads.
    /// </remarks>
    public static bool ShowCreateTeamLink(int teamCount, bool allowTeamCreation, bool showLink = true)
        => teamCount == 0 && allowTeamCreation && showLink;

    /// <summary>
    /// Whether the selector renders the selected team's name instead of a control.
    /// </summary>
    /// <remarks>
    /// One team and it is already selected, so there is nothing left to choose — a dropdown over a single
    /// entry the caller is already inside is a control that cannot do anything.
    /// </remarks>
    /// <param name="teamCount">Teams the caller can see, which for a <c>teams:read</c> holder is every team
    /// in the deployment rather than the ones they belong to.</param>
    /// <param name="hasSelection">Whether a team is currently selected.</param>
    public static bool ShowSelectedTeamName(int teamCount, bool hasSelection)
        => teamCount == 1 && hasSelection;

    /// <summary>
    /// Whether the selector renders the picker. True for every visible-teams state except the one
    /// <see cref="ShowSelectedTeamName"/> claims, so between them no state renders nothing.
    /// </summary>
    /// <remarks>
    /// <b>Includes "teams visible, none selected", which is what Tharga/Team#214 reported.</b> A caller
    /// holding <c>teams:read</c> who belongs to no team sees every team and has selected none, because
    /// <see cref="TeamSelectionResolver"/> deliberately draws its fallback from own memberships only. That
    /// is the right call — defaulting out of the widened set would park an oversight caller inside an
    /// arbitrary tenant they never picked — but the render tree previously covered only
    /// <c>count == 1 &amp;&amp; selected</c> and <c>count &gt; 1 &amp;&amp; selected</c>, so the state fell
    /// through every branch and the top bar drew nothing at all.
    /// <para>
    /// The fix offers the set rather than entering it: the picker appears with a placeholder and no value,
    /// so the caller still chooses explicitly and nothing is selected on their behalf. It covers
    /// <c>count == 1</c> without a selection too — the issue names only the several-teams case, but one
    /// visible team and no selection rendered nothing for the same reason.
    /// </para>
    /// </remarks>
    /// <param name="teamCount">Teams the caller can see.</param>
    /// <param name="hasSelection">Whether a team is currently selected.</param>
    public static bool ShowPicker(int teamCount, bool hasSelection)
        => teamCount > 0 && !ShowSelectedTeamName(teamCount, hasSelection);

    /// <summary>
    /// The team count at and above which the selector offers a search box.
    /// </summary>
    /// <remarks>
    /// Shared with the team list, which uses the same number to decide between cards and a grid. Two
    /// decisions, but both turn on the same fact about the same collection, so they move together.
    /// </remarks>
    public const int DefaultFilterThreshold = TeamListPresentation.DefaultThreshold;

    /// <summary>
    /// Whether the selector offers a search box.
    /// </summary>
    /// <param name="teamCount">Teams the caller can choose between.</param>
    /// <param name="threshold">The count at and above which a filter is worth showing.</param>
    /// <param name="allowFiltering">
    /// A host's explicit answer, which wins outright. Null defers to <paramref name="threshold"/>.
    /// </param>
    /// <remarks>
    /// The same judgement <see cref="Audit.AuditFilterVisibility"/> makes about the audit filter bar —
    /// *"one option is not a filter"* — applied to a different control. Kept here rather than inline in
    /// markup for the reason every decision in this feature is: the project has no bUnit, so a rule left
    /// in a razor file cannot be tested at all.
    /// <para>
    /// A host that forces it on for a caller with one team gets a filter over one team. That is their
    /// call to make and not worth second-guessing — <c>false</c> and <c>true</c> both mean "I have
    /// decided", and the threshold exists precisely for everyone who has not.
    /// </para>
    /// </remarks>
    public static bool ShowFilter(int teamCount, int threshold, bool? allowFiltering = null)
        => allowFiltering ?? TeamListPresentation.IsMany(teamCount, threshold);
}
