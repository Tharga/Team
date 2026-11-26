using Bunit;
using Bunit.TestDoubles;
using Moq;
using Tharga.Team;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Tharga.Team.Blazor.Features.Authentication;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Rendering tests for the components that sit in a host's layout, exercised against the **granular**
/// registration path (<c>AddThargaTeamBlazor</c>) rather than the <c>AddThargaTeam</c> facade.
/// </summary>
/// <remarks>
/// These exist because <c>ValidateOnBuild</c> cannot see them. Blazor resolves <c>@inject</c> properties
/// at render time, not through the constructor graph the validator walks, so a component whose dependency
/// is registered only by the facade builds clean, boots clean, passes health checks and every other test
/// in this suite — and then throws on the first page render, taking the circuit with it (Tharga/Team#157).
/// A rendering assertion is the only thing that catches it.
/// </remarks>
public class GranularPathRenderTests : BunitContext
{
    /// <summary>
    /// The regression guard for #157. <c>LoginDisplay</c> is in the layout, so it renders on every page —
    /// including an anonymous landing page. If this throws, the granular path is unusable.
    /// </summary>
    [Fact]
    public void LoginDisplay_RendersOnTheGranularPath()
    {
        RegisterGranularPath();

        var act = () => Render<LoginDisplay>();

        var ex = Record.Exception(act);
        Assert.True(ex == null, $"LoginDisplay must render after AddThargaTeamBlazor alone, but threw: {ex}");
    }

    /// <summary>
    /// The sign-in menu places the session watch outside its <c>AuthorizeView</c> (Tharga/Team#471), so it is there signed out as well as signed in; with no
    /// session policy it renders nothing.
    /// </summary>
    [Fact]
    public void LoginDisplay_RendersTheSessionWatch_EvenSignedOut()
    {
        RegisterGranularPath();

        var view = Render<LoginDisplay>();

        var watch = view.FindComponent<Tharga.Team.Blazor.Features.Authentication.SessionWatch>();
        Assert.Empty(watch.Markup.Trim());
    }

    /// <summary>
    /// Registers what a host following the documented "Advanced: Step-by-step setup" registers: the
    /// granular entry point, plus the collaborators the host supplies itself.
    /// </summary>
    /// <remarks>
    /// The distinction that makes this test meaningful — <see cref="IUserService"/> is host-supplied (via
    /// <c>RegisterTeamService</c> or <c>AddThargaTeamRepository</c>) and is stubbed here, while everything
    /// the library's own always-on components depend on must come from <c>AddThargaTeamBlazor</c> itself.
    /// Stubbing the latter would hide the very defect this guards.
    /// </remarks>
    private void RegisterGranularPath()
    {
        Services.AddThargaTeamBlazor();
        Services.AddScoped(_ => Mock.Of<IUserService>());

        // Left signed out: an anonymous landing page still renders the layout, which is the cheapest
        // reproduction of #157 and matches the issue's repro steps.
        this.AddAuthorization().SetNotAuthorized();
    }
}
