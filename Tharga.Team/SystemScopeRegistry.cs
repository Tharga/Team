namespace Tharga.Team;

/// <summary>
/// Stores system-level scope definitions. A flat set (no access level) — system keys carry an explicit list,
/// and privileged roles map to a subset.
/// </summary>
public class SystemScopeRegistry : ISystemScopeRegistry
{
    private readonly List<SystemScopeDefinition> _scopes = new();

    public IReadOnlyList<SystemScopeDefinition> All => _scopes;

    /// <summary>
    /// Declares a system scope. Registering a name that is already present is a no-op — the first
    /// registration's description is kept.
    /// </summary>
    /// <param name="scopeName">The scope's name, which is its identity.</param>
    /// <param name="description">Catalogue text shown beside the scope. Ignored if the name is already registered.</param>
    /// <remarks>
    /// <b>A duplicate name is deliberately not an error.</b> The library registers its own built-in scopes,
    /// and a host may register the same name — either because it did so before the library started to
    /// (<c>simulation:demo</c> arrived library-side in 3.14.0, after hosts had been registering it
    /// themselves), or simply because both want the scope to exist. The library cannot guard a call in host
    /// code, so tolerating the duplicate here is the only place the problem can be solved.
    /// <para>
    /// <b>This is not a lost typo check.</b> A misspelled scope produces a new name rather than a duplicate,
    /// so throwing here never caught one; <c>UnregisteredRoleScopeCheck</c> is what reports a role
    /// referencing a scope nobody registered. Nor is there a conflict left to detect: the name is the
    /// capability's identity and the description is catalogue text, so two registrations of one name cannot
    /// disagree about what is being granted. Team scopes differ — <c>ScopeDefinition</c> also carries
    /// <c>DefaultMinimumLevel</c> and <c>GrantOnly</c>, which genuinely can conflict — and
    /// <see cref="ScopeRegistry"/> is unchanged.
    /// </para>
    /// <para>
    /// <b>First registration wins, rather than last.</b> Registration order is not something a host controls
    /// today, so "last wins" would make the rendered catalogue description non-deterministic instead of
    /// controllable. A host that wants to own the wording needs a replace mechanism, which this is not.
    /// </para>
    /// Throwing on a duplicate is what broke <a href="https://github.com/Tharga/Team/issues/237">#237</a>.
    /// </remarks>
    public void Register(string scopeName, string description = null)
    {
        if (_scopes.Any(s => s.Name == scopeName)) return;

        _scopes.Add(new SystemScopeDefinition(scopeName, description));
    }
}
