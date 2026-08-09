using System.Collections.Frozen;
using System.Globalization;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// A component's resolved strings, looked up synchronously by <see cref="TextKey"/>.
/// </summary>
/// <remarks>
/// <b>Why this exists.</b> <see cref="IThargaTextProvider.GetAsync"/> is async and per-key, because an
/// implementation may reach an external content source. Resolving one string at a time means one
/// <c>await</c> and one backing field per label — workable for the four in <c>LoginDisplay</c>, unworkable
/// for a component with forty. A component instead declares its keys, resolves them in a single pass in
/// <c>OnInitializedAsync</c>, and reads them synchronously in markup.
/// <para>
/// An unresolved key returns its own <see cref="TextKey.Default"/> rather than throwing or rendering empty.
/// A missing translation must degrade to English, never to a blank label or a broken page — a provider that
/// throws on one key would otherwise take the whole component down.
/// </para>
/// </remarks>
public sealed class TextSet
{
    private readonly FrozenDictionary<string, string> _values;

    internal TextSet(IReadOnlyDictionary<string, string> values)
    {
        _values = values.ToFrozenDictionary(StringComparer.Ordinal);
    }

    /// <summary>An empty set — every lookup falls back to the key's English default.</summary>
    public static TextSet Empty { get; } = new(new Dictionary<string, string>());

    /// <summary>The resolved string for <paramref name="key"/>, or its English default.</summary>
    public string this[TextKey key]
        => _values.TryGetValue(key.Key, out var value) && !string.IsNullOrEmpty(value) ? value : key.Default;

    /// <summary>
    /// The resolved string for <paramref name="key"/> with <paramref name="args"/> substituted into its
    /// placeholders — the form for a message that names something, e.g. <c>"Email sent to {0}"</c>.
    /// </summary>
    /// <remarks>
    /// <b>Positional placeholders, not interpolation.</b> A translated sentence often needs its parts in a
    /// different order from the English, so the template has to carry <c>{0}</c> / <c>{1}</c> that a
    /// translator can move. An interpolated C# string cannot be translated at all — the text is compiled in.
    /// <para>
    /// <b>Never throws while rendering.</b> A translator can supply a template referencing a placeholder that
    /// does not exist, which would otherwise take the page down at render time on data the toolkit does not
    /// control. A malformed translation falls back to the English default; a malformed default — a toolkit
    /// bug, and covered by tests — falls back to the raw template rather than throwing.
    /// </para>
    /// </remarks>
    public string Format(TextKey key, params object[] args)
    {
        var template = this[key];

        try
        {
            return string.Format(CultureInfo.CurrentCulture, template, args ?? []);
        }
        catch (FormatException)
        {
        }

        try
        {
            return string.Format(CultureInfo.CurrentCulture, key.Default, args ?? []);
        }
        catch (FormatException)
        {
            return key.Default;
        }
    }
}

/// <summary>Resolving many <see cref="TextKey"/> values in one pass.</summary>
public static class ThargaTextProviderExtensions
{
    /// <summary>
    /// Resolves every key through <paramref name="provider"/> and returns them as a synchronously-readable
    /// <see cref="TextSet"/>.
    /// </summary>
    /// <remarks>
    /// <b>One failing key never fails the set.</b> A provider reaching an external source can throw or time
    /// out on any single lookup; that key falls back to its English default and the rest still resolve. A
    /// component rendering one English label among translated ones is a far better outcome than a component
    /// that does not render.
    /// <para>
    /// Duplicate keys are resolved once — a set is usually built from several catalogues that legitimately
    /// share entries, such as an access level named in two places.
    /// </para>
    /// </remarks>
    public static async Task<TextSet> ResolveAsync(this IThargaTextProvider provider, params TextKey[] keys)
    {
        if (provider == null || keys is not { Length: > 0 }) return TextSet.Empty;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key.Key) || values.ContainsKey(key.Key)) continue;

            try
            {
                values[key.Key] = await provider.GetAsync(key);
            }
            catch
            {
                values[key.Key] = key.Default;
            }
        }

        return new TextSet(values);
    }
}
