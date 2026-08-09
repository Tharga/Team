using System.Reflection;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Every localizable string the toolkit renders, with its stable key and English default.
/// </summary>
/// <remarks>
/// <b>This is the list a consumer translates from.</b> Without it a host cannot know what to override — they
/// discover the gaps by finding English text in production, which is how a half-translated product happens.
/// Enumerate <see cref="All"/> to generate a translation table, seed a content system, or assert in your own
/// tests that you have covered everything the toolkit can render:
/// <code>
/// foreach (var key in ThargaTextKeys.All)
///     Console.WriteLine($"{key.Key}\t{key.Default}");
/// </code>
/// <para>
/// <b>Discovered by reflection, not by a hand-written list.</b> A catalogue added later is included without
/// anyone remembering to register it — the same reasoning as <c>OptionsForwarder</c>, and for the same
/// reason: a list somebody must extend is a list that silently falls behind.
/// </para>
/// <para>
/// <b>Keys are whole strings, never a substitutable noun.</b> It is tempting to expose one "team" token and
/// compose sentences from it; that produces broken translations. Swedish suffixes the definite article, so
/// <i>"medlem i ett team"</i> becomes <i>"teamet"</i> — a form no noun substitution reaches — and word order
/// moves besides. Whole strings survive translation; fragments do not.
/// </para>
/// </remarks>
public static class ThargaTextKeys
{
    private static readonly Lazy<IReadOnlyList<TextKey>> _all = new(Discover);

    /// <summary>Every key the toolkit can render, ordered by key.</summary>
    public static IReadOnlyList<TextKey> All => _all.Value;

    private static IReadOnlyList<TextKey> Discover()
        => [.. typeof(ThargaTextKeys).Assembly
            .GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: true, IsSealed: true })
            .SelectMany(t => t.GetFields(BindingFlags.Public | BindingFlags.Static))
            .Where(f => f.IsInitOnly && f.FieldType == typeof(TextKey))
            .Select(f => (TextKey)f.GetValue(null))
            .Where(k => !string.IsNullOrEmpty(k.Key))
            .DistinctBy(k => k.Key, StringComparer.Ordinal)
            .OrderBy(k => k.Key, StringComparer.Ordinal)];
}
