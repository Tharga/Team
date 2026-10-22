using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Features.Scopes;

/// <summary>Resolves scope group headings through the text provider, so a host can localise them.</summary>
public static class ScopeGroupHeadings
{
    /// <summary>
    /// A lookup from group key to heading. A defined group resolves under <see cref="ScopeGroup.TextKeyFor"/> and
    /// falls back to its display name; a key nobody defined shows as itself.
    /// </summary>
    public static async Task<Func<string, string>> ResolveAsync(IThargaTextProvider provider, IReadOnlyList<ScopeGroup> groups)
    {
        var keys = (groups ?? []).ToDictionary(g => g.Key, g => new TextKey(ScopeGroup.TextKeyFor(g.Key), g.DisplayName));
        var set = keys.Count == 0 ? TextSet.Empty : await provider.ResolveAsync([.. keys.Values]);

        return key => keys.TryGetValue(key, out var textKey) ? set[textKey] : key;
    }
}
