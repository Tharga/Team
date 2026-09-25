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
/// Tharga/Team#285: the invitation view reported four different outcomes as "You have no pending
/// invitations". Each outcome now says what happened, and only an open invitation offers to join.
/// </summary>
/// <remarks>
/// <b>The failures are the point.</b> That one sentence is what hid #272 for three releases: the view held the
/// information to say "this link did not resolve" and said "you have nothing waiting" instead, which reads as the
/// recipient's own situation rather than a fault.
/// </remarks>
public class InviteViewOutcomeTests : BunitContext
{
    private const string InviteCode = "6f3a91c2";
    private const string TeamKey = "BodensKommun";
    private const string TeamName = "Bodens kommun";

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

    private readonly Mock<ILocalStorageService> _storage = new();

    private static TeamInvitation Invitation(bool alreadyMember = false, InvitationStatus status = InvitationStatus.Open)
        => new(TeamKey, TeamName, "alice@example.com", alreadyMember) { InviteKey = InviteCode, Status = status };

    [Fact]
    public void NoCode_OnAnInvitesPage_SaysThereIsNothingWaiting()
    {
        var view = Render(code: null, resolved: null, showEmptyMessage: true);

        Assert.Contains(Default(TeamInviteViewText.NoInvitations), Text(view));
        Assert.Empty(view.FindAll("button"));
    }

    /// <summary>Embedded elsewhere, with nothing presented, the view stays out of the way as it always has.</summary>
    [Fact]
    public void NoCode_Embedded_RendersNoMessage()
    {
        var view = Render(code: null, resolved: null, showEmptyMessage: false);

        Assert.DoesNotContain(Default(TeamInviteViewText.NoInvitations), Text(view));
        Assert.DoesNotContain(Default(TeamInviteViewText.InvalidLink), Text(view));
    }

    /// <summary>
    /// Shown whether or not the host asked for the empty message: a code means the visitor followed a link, so
    /// saying nothing is the same defect as saying the wrong thing.
    /// </summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ACodeThatDoesNotResolve_SaysTheLinkIsNoLongerValid(bool showEmptyMessage)
    {
        var view = Render(InviteCode, resolved: null, showEmptyMessage);

        Assert.Contains(Default(TeamInviteViewText.InvalidLink), Text(view));
        Assert.DoesNotContain(Default(TeamInviteViewText.NoInvitations), Text(view));
        Assert.Empty(view.FindAll("button"));
    }

    /// <summary>
    /// Deliberately vague, like the service: unknown, superseded and withdrawn are one answer to someone who
    /// only holds a link. Even an anonymous visitor gets it, because it discloses nothing.
    /// </summary>
    [Fact]
    public void ACodeThatDoesNotResolve_IsReportedToAnAnonymousVisitorToo()
    {
        var view = Render(InviteCode, resolved: null, showEmptyMessage: true, signedIn: false);

        Assert.Contains(Default(TeamInviteViewText.InvalidLink), Text(view));
    }

    /// <summary>An administrator opening their own copied link used to be told there was no invitation.</summary>
    [Fact]
    public void AlreadyAMember_SaysSo_NamingTheTeam_WithNoJoinButton()
    {
        var view = Render(InviteCode, Invitation(alreadyMember: true), showEmptyMessage: true);

        Assert.Contains(string.Format(Default(TeamInviteViewText.AlreadyMember), TeamName), Text(view));
        Assert.DoesNotContain(Default(TeamInviteViewText.NoInvitations), Text(view));
        Assert.Empty(view.FindAll("button"));
    }

    /// <summary>
    /// The expired invitation used to render the join prompt, and the accept was refused after the click.
    /// Specific rather than vague, because resolving already proved the caller holds a real code.
    /// </summary>
    [Fact]
    public void Expired_SaysSo_NamingTheTeam_WithNoJoinButton()
    {
        var view = Render(InviteCode, Invitation(status: InvitationStatus.Expired), showEmptyMessage: true);

        Assert.Contains(string.Format(Default(TeamInviteViewText.Expired), TeamName), Text(view));
        Assert.DoesNotContain(string.Format(Default(TeamInviteViewText.Invitation), TeamName), Text(view));
        Assert.Empty(view.FindAll("button"));
    }

    [Fact]
    public void Open_OffersToJoin()
    {
        var view = Render(InviteCode, Invitation(), showEmptyMessage: true);

        Assert.Contains(string.Format(Default(TeamInviteViewText.Invitation), TeamName), Text(view));
        Assert.Equal(2, view.FindAll("button").Count);
    }

    /// <summary>
    /// None of the three can be answered, so the stored code is dropped; keeping it would replay the same message
    /// on every visit. An open invitation keeps it, so the invitee can sign in and come back.
    /// </summary>
    [Theory]
    [InlineData(false, false, InvitationStatus.Open, true)]
    [InlineData(true, true, InvitationStatus.Open, true)]
    [InlineData(true, false, InvitationStatus.Expired, true)]
    [InlineData(true, false, InvitationStatus.Open, false)]
    public void TheStoredCodeIsClearedUnlessTheInvitationCanBeAnswered(bool resolves, bool alreadyMember, InvitationStatus status, bool cleared)
    {
        Render(InviteCode, resolves ? Invitation(alreadyMember, status) : null, showEmptyMessage: true);

        _storage.Verify(x => x.RemoveItemAsync(Constants.TeamInviteCode, It.IsAny<CancellationToken>()), cleared ? Times.Once() : Times.Never());
        _storage.Verify(x => x.SetItemAsync(Constants.TeamInviteCode, InviteCode, It.IsAny<CancellationToken>()), cleared ? Times.Never() : Times.Once());
    }

    private static string Default(TextKey key) => key.Default;

    private static string Text(IRenderedComponent<TeamInviteView<TestMember>> view) => System.Net.WebUtility.HtmlDecode(view.Markup);

    private IRenderedComponent<TeamInviteView<TestMember>> Render(string code, TeamInvitation resolved, bool showEmptyMessage, bool signedIn = true)
    {
        var invitations = new Mock<ITeamInvitationService>();
        invitations.Setup(x => x.GetInvitationAsync(It.IsAny<string>())).ReturnsAsync(resolved);

        var user = new Mock<IUser>();
        user.SetupGet(x => x.Key).Returns("alice-key");
        var users = new Mock<IUserService>();
        users.Setup(x => x.GetCurrentUserAsync(null)).ReturnsAsync(user.Object);

        Services.AddSingleton(invitations.Object);
        Services.AddSingleton(Mock.Of<ITeamManagementService>());
        Services.AddSingleton(Mock.Of<ITeamStateService>());
        Services.AddSingleton(users.Object);
        Services.AddSingleton(_storage.Object);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton<IOptions<ThargaBlazorOptions>>(Options.Create(new ThargaBlazorOptions()));
        Services.AddRadzenComponents();

        JSInterop.Mode = JSRuntimeMode.Loose;
        var auth = this.AddAuthorization();
        if (signedIn) auth.SetAuthorized("alice");

        var navigation = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo(code == null ? "http://localhost/invitation" : $"http://localhost/invitation?{Constants.TeamInviteToken}={code}");

        return Render<TeamInviteView<TestMember>>(p => p.Add(x => x.ShowEmptyMessage, showEmptyMessage));
    }
}
