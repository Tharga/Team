namespace Tharga.Team;

/// <summary>
/// A host-defined area that scopes are shown under, such as a product area. Defined with
/// <see cref="ScopeRegistry.DefineGroup"/>; the order groups are defined in is the order they are shown in.
/// </summary>
/// <param name="Key">The key scopes name in <see cref="ScopeDefinition.Group"/>.</param>
/// <param name="DisplayName">
/// The heading, in English. Components resolve it through the text provider under
/// <see cref="TextKeyFor"/>, so a host can localise it.
/// </param>
public record ScopeGroup(string Key, string DisplayName)
{
    /// <summary>The text key a component resolves the heading of the group <paramref name="key"/> under.</summary>
    public static string TextKeyFor(string key) => $"team.scopeGroup.{key}";
}
