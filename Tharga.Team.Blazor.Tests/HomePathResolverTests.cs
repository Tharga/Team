using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Where the toolkit goes when it wants the site's landing page (Tharga/Team#287).
/// </summary>
/// <remarks>
/// The sibling of <see cref="InvitePathResolverTests"/>, and it exists separately for the one case the two
/// deliberately disagree about: a blank value is a misconfiguration for an invitation link and the intended
/// answer here.
/// </remarks>
public class HomePathResolverTests
{
    private const string BaseUri = "https://example.com/";

    /// <summary>
    /// Unset is the default and means the application root — which is where <c>UseThargaAuth</c> already
    /// returns people after signing in.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    public void BlankIsTheApplicationRoot(string configured)
    {
        Assert.Equal(BaseUri, HomePathResolver.Resolve(configured, BaseUri));
    }

    /// <summary>
    /// A host writes the route in whichever shape occurs to them, and the base URI already ends in a slash
    /// — so a leading one produces a double slash that reads as a typo nobody made.
    /// </summary>
    [Theory]
    [InlineData("start")]
    [InlineData("/start")]
    [InlineData("start/")]
    [InlineData("/start/")]
    [InlineData("  /start/  ")]
    public void TheSameRouteInAnyShapeResolvesTheSame(string configured)
    {
        Assert.Equal($"{BaseUri}start", HomePathResolver.Resolve(configured, BaseUri));
    }

    /// <summary>A nested route keeps its inner slashes; only the ends are the toolkit's business.</summary>
    [Fact]
    public void ANestedRouteKeepsItsShape()
    {
        Assert.Equal($"{BaseUri}app/dashboard", HomePathResolver.Resolve("/app/dashboard/", BaseUri));
    }

    /// <summary>
    /// The option is unset out of the box, so an existing host gets the application root without doing
    /// anything.
    /// </summary>
    [Fact]
    public void TheOptionDefaultsToUnset()
    {
        Assert.Null(new ThargaBlazorOptions().HomePath);
    }
}
