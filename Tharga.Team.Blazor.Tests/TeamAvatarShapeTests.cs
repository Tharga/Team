using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tharga.Team.Blazor.Features.Team;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The team avatar keeps its declared size inside a flex row, so the badge stays square
/// (Tharga/Team#282).
/// </summary>
/// <remarks>
/// The same defect as <see cref="UserAvatarShapeTests"/> covers for <c>UserAvatar</c> (#279, fixed in
/// #281): a flex item's <c>flex-shrink</c> defaults to 1, and the span's automatic minimum size is the
/// width of the initials, so the flex algorithm may take it below the declared <c>Size</c>.
/// <c>TeamAvatar</c> was not changed at the time and carried the defect for two more releases.
/// </remarks>
public class TeamAvatarShapeTests : BunitContext
{
    private const string DisplayName = "Some Rather Long Team Name";
    private const string PictureUrl = "https://example.com/team.png";
    private const string NoShrink = "flex-shrink:0";

    [Fact]
    public void TheInitialsBadgeDoesNotShrink()
    {
        var avatar = RenderAvatar(resolvedImage: null);

        Assert.Contains(NoShrink, StyleDeclarations(avatar.Find("span")));
    }

    [Fact]
    public void ThePictureDoesNotShrink()
    {
        var avatar = RenderAvatar(new IconImage(PictureUrl));

        Assert.Contains(NoShrink, StyleDeclarations(avatar.Find("img")));
    }

    private IRenderedComponent<TeamAvatar> RenderAvatar(IconImage resolvedImage)
    {
        var resolver = new Mock<IIconResolver>();
        resolver.Setup(x => x.ResolveAsync(It.IsAny<IconSubject>(), It.IsAny<CancellationToken>())).ReturnsAsync(resolvedImage);
        Services.AddSingleton(resolver.Object);

        return Render<TeamAvatar>(parameters => parameters
            .Add(x => x.Name, DisplayName)
            .Add(x => x.Size, "24px"));
    }

    private static string[] StyleDeclarations(AngleSharp.Dom.IElement element)
    {
        return (element.GetAttribute("style") ?? string.Empty)
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(declaration => declaration.Replace(" ", string.Empty))
            .ToArray();
    }
}
