using Blazored.LocalStorage;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// What answering an invitation leaves behind: which team is selected, and where the invitee ends up
/// (Tharga/Team#287).
/// </summary>
/// <remarks>
/// <b>Rendered rather than asserted on a helper, because the defect is in the composition.</b> The pieces
/// are each defensible on their own — the domain raises <c>SelectTeamEvent</c>, the state service bridges
/// it, the view force-navigates so the new claims apply — and the failure only exists where they meet. A
/// test over any one of them stays green while an invitee lands on a blank invitation page with the wrong
/// team selected.
/// <para>
/// The harness fakes <see cref="ITeamManagementService"/>, so no <c>SelectTeamEvent</c> is raised here at
/// all. That is deliberate: it isolates what the <i>view</i> does, which is the half this feature moves.
/// The domain half — that accepting no longer raises the event the view now handles itself — is covered by
/// <c>TeamServiceBaseEventTests</c> in the service suite.
/// </para>
/// </remarks>
public class InviteAcceptLandingTests : BunitContext
{
    private const string InviteCode = "6f3a91c2";
    private const string JoinedTeamKey = "BodensKommun";
    private const string JoinedTeamName = "Bodens kommun";
    private const string UserKey = "alice-key";
    private const string InvitePageUri = "http://localhost/invitation";

    private sealed record TestMember : ITeamMember
    {
        public string Key { get; init; }
        public string Name { get; init; }
        public Invitation Invitation { get; init; }
        public DateTime? LastSeen { get; init; }
        public MembershipState? State { get; init; }
        public AccessLevel AccessLevel { get; init; }
        public string[] TenantRoles { get; init; }
        public string[] ScopeOverrides { get; init; }
    }

    private sealed record TestTeam : ITeam
    {
        public string Key { get; init; }
        public string Name { get; init; }
        public string Icon { get; init; }
    }

    private readonly Mock<ITeamManagementService> _management = new();
    private readonly Mock<ITeamStateService> _state = new();
    private string _homePath;

    /// <summary>The team the invitation is for, as the gated read hands it back once the accept lands.</summary>
    private static readonly TestTeam Joined = new() { Key = JoinedTeamKey, Name = JoinedTeamName };

    [Fact]
    public void AcceptingRecordsTheResponse()
    {
        ClickAccept();

        _management.Verify(x => x.SetInvitationResponseAsync(JoinedTeamKey, UserKey, InviteCode, true), Times.Once);
    }

    /// <summary>
    /// The first half of #287. Accepting an invitation is an explicit choice of that team, and the view
    /// makes nothing of it — so the reload that follows re-runs <c>TeamSelectionResolver</c>, which honours
    /// the remembered key and discards the choice just made.
    /// </summary>
    [Fact]
    public void AcceptingSelectsTheJoinedTeam()
    {
        ClickAccept();

        _state.Verify(x => x.SetSelectedTeamAsync(It.Is<ITeam>(t => t.Key == JoinedTeamKey), false), Times.Once);
    }

    /// <summary>
    /// The second half of #287. With a dedicated <c>InvitePath</c> page — which is what the #191 guidance
    /// tells hosts to set up — reloading the current route lands the invitee back on an invitation page
    /// that now has nothing to show.
    /// </summary>
    [Fact]
    public void AcceptingNavigatesToTheSiteRoot()
    {
        var navigations = ClickAccept();

        Assert.Equal("http://localhost/", navigations.Single());
    }

    /// <summary>
    /// Two navigations on one click is the race this feature removes, so the count is asserted alongside
    /// the destination.
    /// </summary>
    /// <remarks>
    /// The second navigation came from the state service's own reload, reached through an event this
    /// harness fakes away — so what this covers is the view's half of the promise. That the domain no
    /// longer raises that event is covered in the service suite.
    /// </remarks>
    [Fact]
    public void AcceptingNavigatesExactlyOnce()
    {
        var navigations = ClickAccept();

        Assert.Single(navigations);
    }

    [Fact]
    public void DecliningRecordsTheResponse()
    {
        ClickDecline();

        _management.Verify(x => x.SetInvitationResponseAsync(JoinedTeamKey, UserKey, InviteCode, false), Times.Once);
    }

    /// <summary>Declining grants nothing, so it must leave the selection exactly as it found it.</summary>
    [Fact]
    public void DecliningSelectsNothing()
    {
        ClickDecline();

        _state.Verify(x => x.SetSelectedTeamAsync(It.IsAny<ITeam>()), Times.Never);
        _state.Verify(x => x.SetSelectedTeamAsync(It.IsAny<ITeam>(), It.IsAny<bool>()), Times.Never);
    }

    /// <summary>Declining leaves the same page behind as accepting; only the selection differs.</summary>
    [Fact]
    public void DecliningNavigatesToTheSiteRoot()
    {
        var navigations = ClickDecline();

        Assert.Equal("http://localhost/", navigations.Single());
    }

    /// <summary>
    /// A host whose root is not somewhere to land — a marketing page, or an invitation page that is one
    /// step of a longer flow — says where instead.
    /// </summary>
    [Fact]
    public void AConfiguredHomePathIsHonoured()
    {
        _homePath = "/start";

        var navigations = ClickAccept();

        Assert.Equal("http://localhost/start", navigations.Single());
    }

    /// <summary>Declining reads the same option, so the two answers do not part company.</summary>
    [Fact]
    public void AConfiguredHomePathIsHonouredWhenDeclining()
    {
        _homePath = "/start";

        var navigations = ClickDecline();

        Assert.Equal("http://localhost/start", navigations.Single());
    }

    private IReadOnlyList<string> ClickAccept() => ClickButton(0);

    private IReadOnlyList<string> ClickDecline() => ClickButton(1);

    /// <summary>
    /// Where the click sent the invitee, newest first, ignoring the navigation that set the scene.
    /// </summary>
    /// <remarks>
    /// <b>bUnit records history newest-first</b> — a navigation is prepended, not appended — so the
    /// entries a click added are the ones at the front. Reading them off the end instead returns the
    /// navigation that set the scene, which looks exactly like a handler that never navigated at all.
    /// <para>
    /// Waited for rather than read straight after the click, because the handler awaits the service, the
    /// gated read and the selection before it navigates.
    /// </para>
    /// </remarks>
    private IReadOnlyList<string> ClickButton(int index)
    {
        var component = RenderInviteView(out var navigation);
        var before = navigation.History.Count;

        component.FindAll("button")[index].Click();
        component.WaitForState(() => navigation.History.Count > before);

        return navigation.History.Take(navigation.History.Count - before).Select(x => x.Uri).ToArray();
    }

    private IRenderedComponent<TeamInviteView<TestMember>> RenderInviteView(out BunitNavigationManager navigation)
    {
        var invitation = new TeamInvitation(JoinedTeamKey, JoinedTeamName, "alice@example.com", false)
        {
            InviteKey = InviteCode
        };

        var invitations = new Mock<ITeamInvitationService>();
        invitations.Setup(x => x.GetInvitationAsync(InviteCode)).ReturnsAsync(invitation);

        // The gated read the view uses to turn the invitation's key into a team it can select. It passes
        // for a caller whose claims are still stale, because TeamGrantResolver resolves from live
        // membership -- which the accept has just created.
        _management.Setup(x => x.GetTeamByKeyAsync(JoinedTeamKey)).ReturnsAsync(Joined);

        var user = new Mock<IUser>();
        user.SetupGet(x => x.Key).Returns(UserKey);

        var users = new Mock<IUserService>();
        users.Setup(x => x.GetCurrentUserAsync(null)).ReturnsAsync(user.Object);

        Services.AddSingleton(invitations.Object);
        Services.AddSingleton(_management.Object);
        Services.AddSingleton(_state.Object);
        Services.AddSingleton(users.Object);
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton<IOptions<ThargaBlazorOptions>>(Options.Create(new ThargaBlazorOptions { HomePath = _homePath }));
        Services.AddRadzenComponents();

        JSInterop.Mode = JSRuntimeMode.Loose;
        this.AddAuthorization().SetAuthorized("alice");

        navigation = Services.GetRequiredService<NavigationManager>() as BunitNavigationManager;
        navigation.NavigateTo($"{InvitePageUri}?{Constants.TeamInviteToken}={InviteCode}");

        return Render<TeamInviteView<TestMember>>();
    }
}
