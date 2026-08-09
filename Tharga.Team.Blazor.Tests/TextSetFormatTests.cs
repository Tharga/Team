using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// <see cref="TextSet.Format"/> — the form for a message that names something, e.g. "Email sent to {0}".
/// </summary>
/// <remarks>
/// Interpolated C# strings cannot be translated at all: the text is compiled in. A translatable message has
/// to be a template with positional placeholders a translator can reorder, because a translated sentence
/// often needs its parts in a different order from the English.
/// <para>
/// <b>Rendering must never throw.</b> The template can come from a consumer's content system, so it is
/// untrusted input on a render path — these tests pin that a malformed one degrades instead of taking the
/// page down.
/// </para>
/// </remarks>
public class TextSetFormatTests
{
    private sealed class Provider(Dictionary<string, string> values) : IThargaTextProvider
    {
        public Task<string> GetAsync(TextKey key)
            => Task.FromResult(values.TryGetValue(key.Key, out var v) ? v : key.Default);
    }

    private static readonly TextKey Sent = new("test.sent", "Email sent to {0}");
    private static readonly TextKey Two = new("test.two", "Moved {0} to {1}");

    private static async Task<TextSet> Resolve(Dictionary<string, string> values, params TextKey[] keys)
        => await new Provider(values).ResolveAsync(keys);

    [Fact]
    public async Task WithNoTranslation_TheEnglishDefaultIsFormatted()
    {
        var text = await Resolve([], Sent);

        Assert.Equal("Email sent to a@test.com", text.Format(Sent, "a@test.com"));
    }

    [Fact]
    public async Task ATranslationIsFormatted()
    {
        var text = await Resolve(new() { ["test.sent"] = "E-post skickad till {0}" }, Sent);

        Assert.Equal("E-post skickad till a@test.com", text.Format(Sent, "a@test.com"));
    }

    /// <summary>
    /// The reason placeholders are positional rather than interpolated: a translation may need the parts in
    /// a different order, and only a template can express that.
    /// </summary>
    [Fact]
    public async Task ATranslationMayReorderThePlaceholders()
    {
        var text = await Resolve(new() { ["test.two"] = "{1} fick {0}" }, Two);

        Assert.Equal("B fick A", text.Format(Two, "A", "B"));
    }

    /// <summary>
    /// A consumer's content system can return a template naming a placeholder that does not exist. That must
    /// not take the page down on data the toolkit does not control — it falls back to English.
    /// </summary>
    [Fact]
    public async Task AMalformedTranslation_FallsBackToEnglish()
    {
        var text = await Resolve(new() { ["test.sent"] = "Skickad till {0} och {7}" }, Sent);

        Assert.Equal("Email sent to a@test.com", text.Format(Sent, "a@test.com"));
    }

    [Fact]
    public async Task AnUnbalancedBraceInATranslation_FallsBackToEnglish()
    {
        var text = await Resolve(new() { ["test.sent"] = "Skickad till {0" }, Sent);

        Assert.Equal("Email sent to a@test.com", text.Format(Sent, "a@test.com"));
    }

    /// <summary>A key never resolved at all still formats, from its own default.</summary>
    [Fact]
    public void AnUnresolvedKey_FormatsItsDefault()
    {
        Assert.Equal("Email sent to a@test.com", TextSet.Empty.Format(Sent, "a@test.com"));
    }

    [Fact]
    public void NoArguments_DoesNotThrow()
    {
        var plain = new TextKey("test.plain", "Nothing to substitute");

        Assert.Equal("Nothing to substitute", TextSet.Empty.Format(plain));
        Assert.Equal("Nothing to substitute", TextSet.Empty.Format(plain, null));
    }

    /// <summary>
    /// A provider that throws on one key must not fail the whole set — one English label among translated
    /// ones beats a component that does not render.
    /// </summary>
    [Fact]
    public async Task AThrowingProvider_DegradesThatKeyOnly()
    {
        var provider = new ThrowingProvider();

        var text = await provider.ResolveAsync(Sent, Two);

        Assert.Equal("Email sent to x", text.Format(Sent, "x"));
        Assert.Equal("Moved A to B", text.Format(Two, "A", "B"));
    }

    private sealed class ThrowingProvider : IThargaTextProvider
    {
        public Task<string> GetAsync(TextKey key) => throw new InvalidOperationException("content store unreachable");
    }
}
