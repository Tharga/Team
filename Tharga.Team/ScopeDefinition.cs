namespace Tharga.Team;

/// <summary>
/// Defines a scope with its default minimum access level and an optional human-readable description
/// (shown as a tooltip in the scope picker).
/// </summary>
/// <param name="Name">The scope name, as checked by <see cref="RequireScopeAttribute"/>. See the remarks
/// for the grammar it follows.</param>
/// <param name="DefaultMinimumLevel">
/// The least-privileged access level that is granted this scope automatically. Meaningless when
/// <paramref name="GrantOnly"/> is true, since no access level grants such a scope.
/// </param>
/// <param name="Description">Human-readable description, shown in the scope catalogue and pickers.</param>
/// <param name="GrantOnly">
/// When true the scope is registered for documentation and validation only: no access level grants it,
/// a tenant-defined custom role may not reference it, and the scope-override pickers do not offer it.
/// It is held solely through a code-registered tenant role or an explicit scope override, so holding it
/// is a recorded decision rather than a consequence of being a team Owner or Administrator.
/// Register one with <see cref="ScopeRegistry.RegisterGrantOnly"/>.
/// </param>
/// <remarks>
/// <b>A scope name follows a grammar, and it is load-bearing rather than cosmetic.</b>
/// <code>
/// {feature}:{action}            team:read, apikey:manage, audit:read, mcp:discover
/// {feature}:{reach}:{action}    support:unassigned:read, support:all:read
/// </code>
/// <para>
/// <b>Feature</b> is the area acted on and <b>action</b> is what may be done — the two halves the audit
/// log already names, filters and charts by. <b>Reach</b> names the population a grant covers, and is
/// present only where that population is not the one the scope is held against. A team scope needs no
/// reach segment: it is held against a team, and that team is its reach. A system scope has no team to
/// imply one, which is why every reach-bearing scope today is a system scope.
/// </para>
/// <para>
/// <b>The first colon is the one that matters.</b> <c>AuditEntry.ParseScope</c> splits there and nowhere
/// else, so <c>support:all:read</c> is audited as feature <c>support</c>, action <c>all:read</c>. That is
/// why reach goes in the middle: moving it into the feature — <c>support-all:read</c> — would fork one
/// feature into two in the audit log's feature filter and its charts, and would fall outside a
/// <c>support:*</c> notification route.
/// </para>
/// <para>
/// <b>Two scopes express reach differently and are not the shape to copy.</b> <c>teams:read</c>
/// pluralises the feature to mean "every team", against <c>team:read</c>'s one; <c>apikey:system-manage</c>
/// qualifies the action instead. Both predate the grammar, and neither is worth renaming: a renamed scope
/// is invisible to the compiler and silently authorizes nothing, which is what <c>RetiredScopeCheck</c>
/// exists to catch.
/// </para>
/// </remarks>
public record ScopeDefinition(string Name, AccessLevel DefaultMinimumLevel, string Description = null, bool GrantOnly = false);
