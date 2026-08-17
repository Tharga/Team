using Tharga.Mcp;

namespace Tharga.Team.Mcp;

/// <summary>
/// Reads Team-specific caller state off an <see cref="IMcpContext"/>.
/// </summary>
/// <remarks>
/// <see cref="IMcpContext"/> carries the caller's privilege level and nothing else — it deliberately has no
/// identity, so <c>UserId</c>, <c>TeamId</c> and the Developer role are not on it. This bridge supplies the
/// context in the first place (<see cref="HttpContextMcpContextAccessor"/> constructs a
/// <see cref="TeamMcpContext"/>), so its own providers recover that state by asking for the concrete type.
/// <para>
/// The same pattern already appears in <see cref="McpScopeChecker"/>, which matches
/// <c>Current is TeamMcpContext { SelectedTeamScopes: not null }</c>.
/// </para>
/// <para>
/// <b>Returning null when the context is not ours is the fail-closed direction, and every call site depends
/// on it.</b> A caller with no context, or one supplied by a different bridge, yields no identity and no
/// Developer role — so an authorization check written as <c>IsDeveloper == true</c> refuses, and one written
/// as <c>!= true</c> throws. Both are the same answers the previous interface members gave when the context
/// was null.
/// </para>
/// </remarks>
internal static class McpContextExtensions
{
    /// <summary>
    /// The context as this bridge's own <see cref="TeamMcpContext"/>, or null when it came from elsewhere.
    /// </summary>
    internal static TeamMcpContext AsTeamContext(this IMcpContext context) => context as TeamMcpContext;
}
