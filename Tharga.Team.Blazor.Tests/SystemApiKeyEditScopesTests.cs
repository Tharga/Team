using Bunit;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Team;
using Tharga.Team.Blazor.Features.Api;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// A system key's scopes can be changed after it is created, from its row menu, without changing its value (Tharga/Team#522).
/// </summary>
/// <remarks>
/// <b>Rendered and clicked</b>, as the support boards' tests are: a menu item whose handler is never wired compiles, and only clicking it proves it.
/// What the caller may grant is the service's to decide, so these tests check the view carries the choice there and shows the refusal.
/// </remarks>
public class SystemApiKeyEditScopesTests : BunitContext
{
    private const string Read = "support:all:read";
    private const string Manage = "support:all:manage";

    private readonly Mock<ISystemApiKeyManagementService> _keys = new();

    private static IApiKey Key(string id, string name, params string[] scopes)
    {
        var key = new Mock<IApiKey>();
        key.SetupGet(x => x.Key).Returns(id);
        key.SetupGet(x => x.Name).Returns(name);
        key.SetupGet(x => x.SystemScopes).Returns(scopes);
        return key.Object;
    }

    private static async IAsyncEnumerable<IApiKey> Async(params IApiKey[] keys)
    {
        foreach (var key in keys) yield return key;

        await Task.CompletedTask;
    }

    [Fact]
    public void TheRowMenu_OffersEditScopes()
    {
        var view = RenderView(out _);

        OpenMenu(view);

        Assert.Contains("Edit scopes", Menu.Markup);
    }

    [Fact]
    public void WithTheScopePickerTurnedOff_TheRowMenuDoesNotOfferIt()
    {
        var view = RenderView(out _, showScopeOverrides: false);

        OpenMenu(view);

        Assert.DoesNotContain("Edit scopes", Menu.Markup);
    }

    [Fact]
    public async Task TheDialog_IsFilledWithWhatTheKeyHoldsNow_AndOffersEveryRegisteredScope()
    {
        var view = RenderView(out _);

        await OpenDialogAsync(view);

        var picker = Dialogs.FindComponent<RadzenDropDown<IEnumerable<string>>>().Instance;
        Assert.Equal([Read], picker.Value);
        Assert.Equal([Read, Manage], ((IEnumerable<string>)picker.Data).ToArray());
    }

    [Fact]
    public async Task Saving_ChangesTheScopesOfTheSameKey_NeverItsValue()
    {
        var view = RenderView(out var keys);
        await OpenDialogAsync(view);

        await Dialogs.InvokeAsync(() => Dialogs.FindComponent<RadzenDropDown<IEnumerable<string>>>().Instance.ValueChanged.InvokeAsync([Read, Manage]));
        await Dialogs.InvokeAsync(() => Dialogs.Find("[data-save-scopes]").Click());

        view.WaitForAssertion(() => keys.Verify(x => x.SetSystemKeyScopesAsync("k1", It.Is<string[]>(s => s.SequenceEqual(new[] { Read, Manage }))), Times.Once));
        keys.Verify(x => x.RefreshSystemKeyAsync(It.IsAny<string>()), Times.Never);
        keys.Verify(x => x.CreateSystemKeyAsync(It.IsAny<string>(), It.IsAny<string[]>(), It.IsAny<DateTime?>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task ClearingEveryScope_SavesNone()
    {
        var view = RenderView(out var keys);
        await OpenDialogAsync(view);

        await Dialogs.InvokeAsync(() => Dialogs.FindComponent<RadzenDropDown<IEnumerable<string>>>().Instance.ValueChanged.InvokeAsync([]));
        await Dialogs.InvokeAsync(() => Dialogs.Find("[data-save-scopes]").Click());

        view.WaitForAssertion(() => keys.Verify(x => x.SetSystemKeyScopesAsync("k1", It.Is<string[]>(s => s.Length == 0)), Times.Once));
    }

    [Fact]
    public async Task Cancelling_ChangesNothing()
    {
        var view = RenderView(out var keys);
        await OpenDialogAsync(view);

        await Dialogs.InvokeAsync(() => Dialogs.FindComponent<RadzenDropDown<IEnumerable<string>>>().Instance.ValueChanged.InvokeAsync([Read, Manage]));
        await Dialogs.InvokeAsync(() => Dialogs.FindAll("button").First(x => x.TextContent.Contains("Cancel", StringComparison.OrdinalIgnoreCase)).Click());

        keys.Verify(x => x.SetSystemKeyScopesAsync(It.IsAny<string>(), It.IsAny<string[]>()), Times.Never);
    }

    /// <summary>A refusal is the reason the service gave, shown to the person rather than a dialog that closed and did nothing.</summary>
    [Fact]
    public async Task ARefusal_IsShownWithItsReason()
    {
        var view = RenderView(out var keys);
        keys.Setup(x => x.SetSystemKeyScopesAsync("k1", It.IsAny<string[]>()))
            .ThrowsAsync(new UnauthorizedAccessException("This would grant scopes you do not hold yourself: " + Manage + "."));
        await OpenDialogAsync(view);

        await Dialogs.InvokeAsync(() => Dialogs.FindComponent<RadzenDropDown<IEnumerable<string>>>().Instance.ValueChanged.InvokeAsync([Read, Manage]));
        await Dialogs.InvokeAsync(() => Dialogs.Find("[data-save-scopes]").Click());

        Notifications.WaitForAssertion(() => Assert.Contains("you do not hold yourself", Notifications.Markup));
    }

    [Fact]
    public async Task AfterASave_TheListIsReadAgain()
    {
        var view = RenderView(out var keys);
        await OpenDialogAsync(view);
        keys.Invocations.Clear();

        await Dialogs.InvokeAsync(() => Dialogs.FindComponent<RadzenDropDown<IEnumerable<string>>>().Instance.ValueChanged.InvokeAsync([Read, Manage]));
        await Dialogs.InvokeAsync(() => Dialogs.Find("[data-save-scopes]").Click());

        view.WaitForAssertion(() => keys.Verify(x => x.GetSystemKeysAsync(It.IsAny<PrivateKeyScope>()), Times.AtLeastOnce));
    }

    private IRenderedComponent<RadzenContextMenu> Menu { get; set; }
    private IRenderedComponent<RadzenDialog> Dialogs { get; set; }
    private IRenderedComponent<RadzenNotification> Notifications { get; set; }

    private IRenderedComponent<SystemApiKeyView> RenderView(out Mock<ISystemApiKeyManagementService> keys, bool showScopeOverrides = true)
    {
        keys = _keys;
        _keys.Setup(x => x.GetSystemKeysAsync(It.IsAny<PrivateKeyScope>())).Returns(() => Async(Key("k1", "Integration", Read)));

        var registry = new SystemScopeRegistry();
        registry.Register(Read, "Read cases");
        registry.Register(Manage, "Answer cases");

        Services.AddSingleton(_keys.Object);
        Services.AddSingleton<ISystemScopeRegistry>(registry);
        Services.AddSingleton(Options.Create(new ApiKeyOptions()));
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        var authorization = this.AddAuthorization();
        authorization.SetAuthorized("alice");
        authorization.SetRoles(Roles.Developer);

        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", true));

        Menu = Render<RadzenContextMenu>();
        Dialogs = Render<RadzenDialog>();
        Notifications = Render<RadzenNotification>();

        return Render<SystemApiKeyView>(p => p.Add(x => x.ShowScopeOverrides, showScopeOverrides));
    }

    private void OpenMenu(IRenderedComponent<SystemApiKeyView> view)
        => view.Find("button .rz-button-icon-left, button").Click();

    private async Task OpenDialogAsync(IRenderedComponent<SystemApiKeyView> view)
    {
        OpenMenu(view);
        var item = Menu.FindAll("li, .rz-menuitem").First(x => x.TextContent.Contains("Edit scopes"));
        await Menu.InvokeAsync(() => item.Click());

        Dialogs.WaitForAssertion(() => Assert.NotEmpty(Dialogs.FindAll("[data-edit-scopes]")));
    }
}
