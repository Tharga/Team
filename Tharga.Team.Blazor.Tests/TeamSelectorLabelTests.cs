using Bunit;
using MongoDB.Bson;
using Bunit.TestDoubles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Radzen;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.MongoDB;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The team selector puts a team's name in the DOM, never the team (Tharga/Team#254).
/// </summary>
/// <remarks>
/// Radzen's dropdown renders a hidden accessible input whose <c>value</c> and <c>aria-label</c> both come
/// from <c>Value.ToString()</c>, and <c>TextProperty</c> does not reach it. Bound to a record, that is the
/// synthesized member dump — the entity id, the consent access level and any external URL a host's own
/// entity carries, on every page the selector appears on.
/// <para>
/// <b>Why this renders rather than asserting on the entity.</b> <see cref="TeamEntityToStringTests"/> in the
/// MongoDB suite covers the entity, and it is the fix. This covers the composition, which is the part that
/// can regress without the entity changing at all: binding something else to the dropdown, or a Radzen
/// version deriving another attribute from <c>ToString()</c>, both leave that test green and put the dump
/// back in the DOM. It is bound to the real <see cref="DefaultTeamEntity"/> for the same reason — a
/// test-local stand-in would only re-prove Radzen's behaviour, not ours.
/// </para>
/// </remarks>
public class TeamSelectorLabelTests : BunitContext
{
    private const string TeamName = "Arjeplogs kommun";
    private const string OtherTeamName = "Bodens kommun";
    private const string IconUrl = "https://yt3.ggpht.com/ytc/AKedOLT54s64ewm5vaxOp1KxLwCGH1DiVXMMm39HXfdY0g";

    private static readonly DefaultTeamEntity Selected = new()
    {
        Id = ObjectId.Parse("6a6f5433317a6a5d3720a978"),
        Key = "ArjeplogsKommun",
        Name = TeamName,
        Members = [],
        ConsentAccessLevel = AccessLevel.Administrator,
        Icon = IconUrl
    };

    private static readonly DefaultTeamEntity Other = new()
    {
        Key = "BodensKommun",
        Name = OtherTeamName,
        Members = []
    };

    /// <summary>The team's name is what the control presents — the behaviour the fix must not cost.</summary>
    [Fact]
    public void TheSelectorShowsTheTeamName()
    {
        var markup = RenderSelector();

        Assert.Contains(TeamName, markup);
    }

    /// <summary>
    /// The defect itself. A synthesized record <c>ToString()</c> opens with <c>"DefaultTeamEntity { "</c>,
    /// so the type name followed by a brace is the dump's signature wherever it lands.
    /// </summary>
    [Fact]
    public void TheEntityIsNotDumpedIntoTheMarkup()
    {
        var markup = RenderSelector();

        Assert.DoesNotContain($"{nameof(DefaultTeamEntity)} {{", markup);
    }

    /// <summary>
    /// The exposure half, asserted on the values rather than on the dump's shape: whatever Radzen renders,
    /// these must not be in the page.
    /// </summary>
    [Theory]
    [InlineData("6a6f5433317a6a5d3720a978")]
    [InlineData("Administrator")]
    [InlineData(IconUrl)]
    public void InternalStateIsNotExposedInTheMarkup(string secret)
    {
        var markup = RenderSelector();

        Assert.DoesNotContain(secret, markup);
    }

    private string RenderSelector()
    {
        ITeam[] teams = [Selected, Other];

        var directory = new Mock<ITeamDirectoryService>();
        directory.Setup(x => x.GetTeamsAsync()).Returns(teams.ToAsyncEnumerable());
        directory.Setup(x => x.IsSuspendedAsync(It.IsAny<string>())).ReturnsAsync(false);

        var state = new Mock<ITeamStateService>();
        state.Setup(x => x.GetSelectedTeamAsync()).ReturnsAsync(Selected);

        Services.AddSingleton(directory.Object);
        Services.AddSingleton(state.Object);
        Services.AddSingleton(Mock.Of<ITeamOversightService>());
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton<IOptions<ThargaBlazorOptions>>(Options.Create(new ThargaBlazorOptions()));
        Services.AddRadzenComponents();

        // Radzen's dropdown calls Radzen.preventArrows on first render; strict mode throws on it.
        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddAuthorization().SetAuthorized("alice");

        return Render<TeamSelector>().Markup;
    }
}
