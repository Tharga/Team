using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tharga.Team.Blazor.Features.User;

namespace Tharga.Team.Blazor.Tests;

/// <summary>The avatar keeps its declared size inside a flex row, so it stays round (Tharga/Team#279).</summary>
public class UserAvatarShapeTests : BunitContext
{
    private const string DisplayName = "Some Rather Long Display Name";
    private const string PictureUrl = "https://example.com/avatar.png";
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

    private IRenderedComponent<UserAvatar> RenderAvatar(IconImage resolvedImage)
    {
        var resolver = new Mock<IIconResolver>();
        resolver.Setup(x => x.ResolveAsync(It.IsAny<IconSubject>(), It.IsAny<CancellationToken>())).ReturnsAsync(resolvedImage);
        Services.AddSingleton(resolver.Object);

        return Render<UserAvatar>(parameters => parameters
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
