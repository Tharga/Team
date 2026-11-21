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
        key.SetupGet(x => x.ApiKey).Returns("secret"); // a key with a value, so that Lock is offered too
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

    // --- what the view offers a caller who holds less than a key does (Tharga/Team#540, #541) ---

    private static readonly string[] WholeKeyActions = ["Edit scopes", "Lock", "Refresh", "Disable", "Delete"];

    /// <summary>The service refuses every act on a key that holds more than the caller does, so the menu does not offer them.</summary>
    [Fact]
    public void AKeyThatHoldsMoreThanTheCaller_OffersNoChange()
    {
        var view = RenderView(out _, held: [Read], keyScopes: [Read, Manage]);

        OpenMenu(view);

        foreach (var action in WholeKeyActions) Assert.DoesNotContain(action, Menu.Markup);
    }

    /// <summary>A refusal that reveals nothing is not enough: the person is told why the row has no actions.</summary>
    [Fact]
    public void AKeyThatHoldsMoreThanTheCaller_SaysWhyOnItsActionsButton()
    {
        var view = RenderView(out _, held: [Read], keyScopes: [Read, Manage]);

        var title = view.Find("button.rz-button").GetAttribute("title");

        Assert.Contains(Manage, title);
        Assert.DoesNotContain(Read + ",", title);
    }

    [Fact]
    public void AKeyWithinWhatTheCallerHolds_OffersEveryChange()
    {
        var view = RenderView(out _, held: [Read, Manage], keyScopes: [Read]);

        OpenMenu(view);

        foreach (var action in WholeKeyActions) Assert.Contains(action, Menu.Markup);
    }

    [Fact]
    public void AnOverseer_IsOfferedEveryChangeOnAnyKey()
    {
        var view = RenderView(out _, held: [SystemApiKeyScopes.Oversee], keyScopes: [Read, Manage]);

        OpenMenu(view);

        foreach (var action in WholeKeyActions) Assert.Contains(action, Menu.Markup);
    }

    [Fact]
    public void AKeyWithNoScopes_IsOfferedEveryChange_ToACallerHoldingNothing()
    {
        var view = RenderView(out _, held: [], keyScopes: []);

        OpenMenu(view);

        foreach (var action in WholeKeyActions) Assert.Contains(action, Menu.Markup);
    }

    /// <summary>Copying and the audit log are not changes, so a key above the caller keeps them.</summary>
    [Fact]
    public void AKeyThatHoldsMoreThanTheCaller_KeepsWhatIsNotAChange()
    {
        var view = RenderView(out _, held: [Read], keyScopes: [Read, Manage], showAuditLogButton: true);

        OpenMenu(view);

        Assert.Contains("audit", Menu.Markup, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> DisabledOptions(IRenderedComponent<RadzenDropDown<IEnumerable<string>>> picker, IRenderedComponent<RadzenDropDown<IEnumerable<string>>> open = null)
        => (open ?? picker).FindAll(".rz-dropdown-item.rz-state-disabled, .rz-multiselect-item.rz-state-disabled").Select(x => x.TextContent.Trim());

    /// <summary>The picker lists every registered scope, but one the caller does not hold cannot be ticked, since the service would refuse it.</summary>
    [Fact]
    public void TheCreatePicker_DisablesScopesTheCallerDoesNotHold()
    {
        var view = RenderView(out _, held: [Read], keyScopes: [Read]);
        var picker = view.FindComponent<RadzenDropDown<IEnumerable<string>>>();
        picker.Find(".rz-dropdown").Click();

        var disabled = DisabledOptions(picker).ToArray();

        Assert.Equal([Manage], disabled);
    }

    [Fact]
    public void TheCreatePicker_DisablesNothingForAnOverseer()
    {
        var view = RenderView(out _, held: [SystemApiKeyScopes.Oversee], keyScopes: [Read]);
        var picker = view.FindComponent<RadzenDropDown<IEnumerable<string>>>();
        picker.Find(".rz-dropdown").Click();

        Assert.Empty(DisabledOptions(picker));
    }

    [Fact]
    public async Task TheEditPicker_DisablesScopesTheCallerDoesNotHold()
    {
        var view = RenderView(out _, held: [Read], keyScopes: [Read]);
        await OpenDialogAsync(view);
        var picker = Dialogs.FindComponent<RadzenDropDown<IEnumerable<string>>>();
        picker.Find(".rz-dropdown").Click();

        Assert.Equal([Manage], DisabledOptions(picker));
    }

    private IRenderedComponent<SystemApiKeyView> RenderView(out Mock<ISystemApiKeyManagementService> keys, bool showScopeOverrides = true, string[] held = null, string[] keyScopes = null, bool showAuditLogButton = false)
    {
        held ??= [Read, Manage];
        keyScopes ??= [Read];
        keys = _keys;
        _keys.Setup(x => x.GetSystemKeysAsync(It.IsAny<PrivateKeyScope>())).Returns(() => Async(Key("k1", "Integration", keyScopes)));

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
        authorization.SetClaims(held.Select(s => new System.Security.Claims.Claim(TeamClaimTypes.SystemScope, s)).ToArray());

        SetRendererInfo(new Microsoft.AspNetCore.Components.RendererInfo("Server", true));

        Menu = Render<RadzenContextMenu>();
        Dialogs = Render<RadzenDialog>();
        Notifications = Render<RadzenNotification>();

        return Render<SystemApiKeyView>(p => p.Add(x => x.ShowScopeOverrides, showScopeOverrides).Add(x => x.ShowAuditLogButton, showAuditLogButton));
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
