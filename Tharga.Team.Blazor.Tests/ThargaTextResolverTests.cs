using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>Text written outside a component — a mail footer, the unsubscribe page — is translated by the same provider as the components.</summary>
public class ThargaTextResolverTests
{
    [Fact]
    public async Task AKey_IsAnsweredByTheRegisteredTextProvider()
    {
        var provider = new Mock<IThargaTextProvider>();
        provider.Setup(x => x.GetAsync(new TextKey(UserMailTexts.PageConfirmKey, UserMailTexts.PageConfirm))).ReturnsAsync("Avsluta");

        Assert.Equal("Avsluta", await new ThargaTextResolver(provider.Object).GetAsync(UserMailTexts.PageConfirmKey, UserMailTexts.PageConfirm));
    }

    [Fact]
    public async Task AProviderThatFailsOrHasNothing_LeavesTheEnglish()
    {
        var failing = new Mock<IThargaTextProvider>();
        failing.Setup(x => x.GetAsync(It.IsAny<TextKey>())).ThrowsAsync(new InvalidOperationException());
        var empty = new Mock<IThargaTextProvider>();
        empty.Setup(x => x.GetAsync(It.IsAny<TextKey>())).ReturnsAsync(string.Empty);

        Assert.Equal("Unsubscribe", await new ThargaTextResolver(failing.Object).GetAsync("k", "Unsubscribe"));
        Assert.Equal("Unsubscribe", await new ThargaTextResolver(empty.Object).GetAsync("k", "Unsubscribe"));
    }

    [Fact]
    public void TheBlazorRegistration_SuppliesTheResolver()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddScoped(_ => Mock.Of<AuthenticationStateProvider>());
        services.AddScoped(_ => Mock.Of<NavigationManager>());
        services.AddThargaTeamBlazor();

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<ThargaTextResolver>(scope.ServiceProvider.GetRequiredService<ITextResolver>());
    }
}
