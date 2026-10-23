using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.User;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>"Change email": the address shown to edit, validated before it is sent, saved through the management service, and any refusal shown without closing.</summary>
public class UserEmailDialogRenderTests : BunitContext
{
    private const string UserKey = "u-1";
    private const string Current = "alice@example.com";

    private readonly Mock<IUserManagementService> _management = new();
    private Task<dynamic> _opened;

    /// <remarks>The dialog service completes a dialog only when one is open, so a placeholder stands in for the one a view would have opened.</remarks>
    private IRenderedComponent<UserEmailDialog> Render(string current = Current, bool directoryLinked = false, IThargaTextProvider texts = null)
    {
        _management.Setup(x => x.SetUserEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(new UserEmailChangeResult(Current));

        Services.AddSingleton(_management.Object);
        Services.AddSingleton(texts ?? new DefaultThargaTextProvider());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        _opened = Services.GetRequiredService<DialogService>().OpenAsync("Change email", _ => (RenderFragment)(_ => { }));

        return Render<UserEmailDialog>(p => p
            .Add(x => x.UserKey, UserKey)
            .Add(x => x.CurrentEmail, current)
            .Add(x => x.DirectoryLinked, directoryLinked));
    }

    private static void Type(IRenderedComponent<UserEmailDialog> view, string address) => view.Find("input").Change(address);

    private static void Save(IRenderedComponent<UserEmailDialog> view) => view.Find("form").Submit();

    [Fact]
    public void TheCurrentAddress_IsShownToEdit()
    {
        var view = Render();

        Assert.Equal(Current, view.Find("input").GetAttribute("value"));
        Assert.Contains("Nothing is sent to it.", view.Markup);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("unknown")]
    public void AUserWithNoRealAddress_GetsAnEmptyBox(string current)
    {
        var view = Render(current);

        Assert.True(string.IsNullOrEmpty(view.Find("input").GetAttribute("value")));
    }

    [Fact]
    public void TheDirectoryNote_IsShownOnlyForAUserLinkedToTheDirectory()
    {
        Assert.NotEmpty(Render(directoryLinked: true).FindAll("[data-directory-note]"));
    }

    [Fact]
    public void ForAUserWithNoDirectoryLink_ThereIsNoDirectoryNote()
    {
        Assert.Empty(Render(directoryLinked: false).FindAll("[data-directory-note]"));
    }

    [Fact]
    public async Task Saving_SetsTheAddress_AndClosesWithTrue()
    {
        var view = Render();

        Type(view, "alice.new@example.com");
        Save(view);

        Assert.True((bool)await _opened.WaitAsync(TimeSpan.FromSeconds(10)));
        _management.Verify(x => x.SetUserEmailAsync(UserKey, "alice.new@example.com", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ARefusalFromTheService_IsShown_AndTheDialogStaysOpen()
    {
        var view = Render();
        _management.Setup(x => x.SetUserEmailAsync(UserKey, "x@example.com", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentException("The email address is not a single, plain address."));

        Type(view, "x@example.com");
        Save(view);

        view.WaitForAssertion(() => Assert.Contains("The email address is not a single, plain address.", view.Find("[data-error]").TextContent));
        Assert.False(_opened.IsCompleted);
    }

    [Fact]
    public void AnEmptyAddress_IsNotSent_AndSaysItIsRequired()
    {
        var view = Render();

        Type(view, "");
        Save(view);

        Assert.Contains("An email address is required.", view.Markup);
        _management.Verify(x => x.SetUserEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(_opened.IsCompleted);
    }

    [Fact]
    public void AnAddressThatIsNoAddress_IsNotSent_AndSaysSo()
    {
        var view = Render();

        Type(view, "not-an-address");
        Save(view);

        Assert.Contains("Enter a valid email address.", view.Markup);
        _management.Verify(x => x.SetUserEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        Assert.False(_opened.IsCompleted);
    }

    [Fact]
    public async Task Cancelling_ClosesWithFalse_AndChangesNothing()
    {
        var view = Render();

        view.Find("button[type=button]").Click();

        Assert.False((bool)await _opened.WaitAsync(TimeSpan.FromSeconds(10)));
        _management.Verify(x => x.SetUserEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public void TheTexts_AreTranslatedThroughTheTextProvider()
    {
        var view = Render(texts: new Translating(new Dictionary<string, string>
        {
            [UserEmailDialogText.Email.Key] = "E-postadress",
            [UserEmailDialogText.Save.Key] = "Spara"
        }));

        Assert.Contains("E-postadress", view.Markup);
        Assert.Contains("Spara", view.Markup);
    }

    private sealed class Translating(Dictionary<string, string> texts) : IThargaTextProvider
    {
        public Task<string> GetAsync(TextKey key) => Task.FromResult(texts.GetValueOrDefault(key.Key) ?? key.Default);
    }
}
