using System.Reflection;
using Tharga.Team.Blazor.Features.Simulation;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The access-simulation banner resolves its wording like the card next to it. Tharga/Team#221.
/// </summary>
/// <remarks>
/// The banner is the most prominent thing on screen during a simulation and carries the way out, so for a
/// Swedish-first host it was the one part of the feature guaranteed to stay in English — and
/// "Return to my access" is the control someone needs to find under pressure.
/// <para>
/// <b>The sentence is one key, not three.</b> "Viewing as {0} — your own access is reduced." keeps word
/// order with the translator; assembling it from a "viewing as" fragment and a "reduced" fragment would
/// hard-code English order into every translation, which is the mistake <see cref="ThargaTextKeys"/>
/// warns about in its own remarks.
/// </para>
/// </remarks>
public class AccessSimulationBarTextTests
{
    [Fact]
    public void EveryKeyIsInAll()
    {
        var declared = typeof(AccessSimulationBarText)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.FieldType == typeof(TextKey))
            .Select(f => (TextKey)f.GetValue(null))
            .ToArray();

        Assert.NotEmpty(declared);
        Assert.All(declared, key => Assert.Contains(AccessSimulationBarText.All, k => k.Key == key.Key));
    }

    /// <summary>Without this a consumer cannot generate a translation table that includes the banner.</summary>
    [Fact]
    public void EveryKeyIsDiscoverableFromTheToolkitCatalogue()
    {
        Assert.All(AccessSimulationBarText.All, key => Assert.Contains(ThargaTextKeys.All, k => k.Key == key.Key));
    }

    [Fact]
    public void KeysAreNamespacedToTheBarAndDoNotCollideWithTheCard()
    {
        Assert.All(AccessSimulationBarText.All, k => Assert.StartsWith("team.simulation.bar.", k.Key, StringComparison.Ordinal));
        Assert.All(AccessSimulationCardText.All, k => Assert.StartsWith("team.simulation.card.", k.Key, StringComparison.Ordinal));
    }

    // --- the composed sentence ---

    [Fact]
    public void TheBannerSentenceSplitsAroundTheTarget()
    {
        var (before, after) = AccessSimulationBannerSentence.SplitAround("Viewing as {0} — your own access is reduced.");

        Assert.Equal("Viewing as ", before);
        Assert.Equal(" — your own access is reduced.", after);
    }

    /// <summary>
    /// A translation is host-supplied text the toolkit does not control. One that drops the placeholder must
    /// degrade to a sentence without the name — never throw inside a banner that is itself the way out of a
    /// reduced session.
    /// </summary>
    [Fact]
    public void ASentenceWithoutThePlaceholderKeepsItsWordsAndNamesNobody()
    {
        var (before, after) = AccessSimulationBannerSentence.SplitAround("Din behörighet är begränsad.");

        Assert.Equal("Din behörighet är begränsad.", before);
        Assert.Equal(string.Empty, after);
    }

    [Fact]
    public void ASentenceThatIsOnlyThePlaceholderRendersJustTheTarget()
    {
        var (before, after) = AccessSimulationBannerSentence.SplitAround("{0}");

        Assert.Equal(string.Empty, before);
        Assert.Equal(string.Empty, after);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void AnEmptySentenceIsNotAnError(string template)
    {
        var (before, after) = AccessSimulationBannerSentence.SplitAround(template);

        Assert.Equal(string.Empty, before);
        Assert.Equal(string.Empty, after);
    }

    /// <summary>
    /// The defaults have to survive their own mechanism, or the English fallback is the thing that breaks.
    /// </summary>
    [Fact]
    public void TheEnglishDefaultCarriesThePlaceholder()
    {
        var (before, after) = AccessSimulationBannerSentence.SplitAround(AccessSimulationBarText.ViewingAs.Default);

        Assert.NotEqual(string.Empty, before);
        Assert.NotEqual(string.Empty, after);
    }

    // --- describing what is being simulated ---

    [Fact]
    public void ARoleTargetIsNamedThroughItsOwnKey()
    {
        var text = TextSet.Empty;
        var simulation = new AccessSimulation { Kind = AccessSimulationKind.Role, Label = "Registrar", Scopes = [] };

        Assert.Equal("the Registrar role", AccessSimulationBannerSentence.Describe(simulation, text));
    }

    [Fact]
    public void AnAccessLevelTargetIsNamedThroughItsOwnKey()
    {
        var text = TextSet.Empty;
        var simulation = new AccessSimulation { Kind = AccessSimulationKind.AccessLevel, Label = "Viewer", Scopes = [] };

        Assert.Equal("access level Viewer", AccessSimulationBannerSentence.Describe(simulation, text));
    }

    /// <summary>A member is named, not described — there is no English around them to translate.</summary>
    [Fact]
    public void AMemberTargetIsJustTheirName()
    {
        var text = TextSet.Empty;
        var simulation = new AccessSimulation { Kind = AccessSimulationKind.User, Label = "Bob", Scopes = [] };

        Assert.Equal("Bob", AccessSimulationBannerSentence.Describe(simulation, text));
    }
}
