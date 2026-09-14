using Tharga.Team;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Decides whether a Roles picker is offered, for every surface that assigns tenant roles.
/// </summary>
/// <remarks>
/// Pure and static so it is unit-testable, and shared so <c>ApiKeyView</c> and <c>TeamComponent</c> answer the
/// question the same way. They used to answer it differently — one by whether a role service or registry was
/// registered, the other by whether the registry was — which is how they would drift.
/// </remarks>
internal static class RolePickerGate
{
    /// <summary>
    /// Whether the Roles picker is offered: the host enabled it and the resolved role set has something in it.
    /// </summary>
    /// <remarks>
    /// Gating on a registered role source alone offered a host that registers the service but defines no roles
    /// a picker that can only ever be set to nothing (Tharga/Team#275). Roles a principal already holds but that
    /// are absent from <paramref name="roles"/> are hidden roles, which the picker never shows anyway.
    /// </remarks>
    /// <param name="showRoles">The component's <c>ShowRoles</c> parameter.</param>
    /// <param name="roles">The roles resolved for the team in view — already merged and visibility-filtered.</param>
    public static bool ShowRoles(bool showRoles, IReadOnlyCollection<TenantRoleDefinition> roles)
        => showRoles && roles is { Count: > 0 };
}
