using Tharga.Team;

namespace Tharga.Team.Blazor.Features.Simulation;

/// <summary>
/// What the simulation dialog has selected: an access level, roles, scopes ticked by hand, and — when the
/// selection came from picking a member and was not edited since — that member's name.
/// </summary>
/// <param name="AccessLevel">The chosen level, or null for none.</param>
/// <param name="Roles">The chosen tenant roles.</param>
/// <param name="Scopes">Scopes ticked by hand. Scopes the level or roles already grant may be included; they are not counted twice.</param>
/// <param name="MemberName">The member the selection was filled from, while it is unedited. Null otherwise.</param>
public sealed record SimulationSelection(
    AccessLevel? AccessLevel,
    IReadOnlyList<string> Roles,
    IReadOnlyList<string> Scopes,
    string MemberName = null);

/// <summary>
/// Composes one <see cref="AccessSimulation"/> from an access level, roles and hand-ticked scopes.
/// </summary>
/// <remarks>
/// The target is <c>level scopes ∪ role scopes ∪ ticked scopes</c>. It is still only a request: the filter
/// keeps what the caller also holds and removes the rest, so composing more here cannot show more.
/// </remarks>
internal static class AccessSimulationComposer
{
    /// <summary>The label for a composition with nothing in it, matching <see cref="AccessSimulationTargets.FromScopes"/>.</summary>
    public const string EmptyLabel = "no scopes";

    private const string LabelSeparator = " + ";

    /// <summary>
    /// The scopes the chosen level and roles grant — what the dialog shows as checked and not untickable.
    /// </summary>
    public static IReadOnlyCollection<string> Inherited(
        AccessLevel? accessLevel,
        IEnumerable<string> roles,
        IReadOnlyDictionary<AccessLevel, IReadOnlyList<string>> levelScopes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> roleScopes)
    {
        var fromLevel = accessLevel != null && levelScopes.TryGetValue(accessLevel.Value, out var ls) ? ls : [];
        var fromRoles = (roles ?? []).SelectMany(r => roleScopes.TryGetValue(r, out var rs) ? rs : []);

        return fromLevel.Concat(fromRoles).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Builds the simulation for <paramref name="selection"/>.</summary>
    public static AccessSimulation Build(
        SimulationSelection selection,
        IReadOnlyDictionary<AccessLevel, IReadOnlyList<string>> levelScopes,
        IReadOnlyDictionary<string, IReadOnlyList<string>> roleScopes)
    {
        var roles = selection.Roles ?? [];
        var inherited = Inherited(selection.AccessLevel, roles, levelScopes, roleScopes);
        var extra = (selection.Scopes ?? []).Where(s => !inherited.Contains(s)).Distinct(StringComparer.Ordinal).ToArray();

        var fromMember = !string.IsNullOrEmpty(selection.MemberName);

        return new AccessSimulation
        {
            Kind = fromMember ? AccessSimulationKind.User : AccessSimulationKind.Composed,
            Label = fromMember ? selection.MemberName : ComposedLabel(selection.AccessLevel, roles, extra),
            Scopes = [.. inherited.Concat(extra)],
            AccessLevel = selection.AccessLevel,
            DropSystemScopes = true,
            DropAppRoles = true
        };
    }

    private static string ComposedLabel(AccessLevel? accessLevel, IEnumerable<string> roles, IEnumerable<string> extraScopes)
    {
        var parts = new[] { accessLevel?.ToString() }.Concat(roles).Concat(extraScopes).Where(p => !string.IsNullOrEmpty(p)).ToArray();
        return parts.Length == 0 ? EmptyLabel : string.Join(LabelSeparator, parts);
    }
}
