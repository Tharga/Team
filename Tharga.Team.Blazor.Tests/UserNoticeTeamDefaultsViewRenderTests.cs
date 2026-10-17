using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Radzen.Blazor;
using Tharga.Team.Blazor.Features.Team;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>"Notice defaults": the selected team's default for every notice, saving at once for that team, and a reader without the scope told so.</summary>
public class UserNoticeTeamDefaultsViewRenderTests : BunitContext
{
    private const string TeamKey = "team-1";

    private static readonly UserNoticeType Opened = new()
    {
        Key = "case.opened",
        Name = "A case is opened in my team",
        Category = "work",
        Audience = UserNoticeAudience.WithScope("case:read"),
        Deliveries = [UserNoticeDelivery.AtOnce, UserNoticeDelivery.Digest],
        DefaultDelivery = UserNoticeDelivery.Digest,
        Subject = "New case: {subject}"
    };

    private static readonly UserNoticeType Joined = new()
    {
        Key = "member.joined",
        Name = "Someone joins my team",
        Category = "work",
        Audience = UserNoticeAudience.AtLeast(AccessLevel.Administrator),
        Subject = "{subject} joined"
    };

    private readonly Mock<IUserNoticeTeamService> _service = new();

    private IRenderedComponent<UserNoticeTeamDefaultsView> Render(string selectedTeam = TeamKey, bool permitted = true, string teamKey = null)
    {
        var defaults = new[]
        {
            new UserNoticeTeamDefault { Notice = Opened, Enabled = true, Delivery = UserNoticeDelivery.AtOnce, IsSet = true },
            new UserNoticeTeamDefault { Notice = Joined, Enabled = true, Delivery = UserNoticeDelivery.AtOnce }
        };

        if (permitted) _service.Setup(x => x.GetTeamDefaultsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(defaults);
        else _service.Setup(x => x.GetTeamDefaultsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());

        _service.Setup(x => x.SetTeamDefaultAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<UserNoticeDelivery>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string _, string key, bool enabled, UserNoticeDelivery delivery, CancellationToken _) => new UserNoticeTeamDefault
            {
                Notice = key == Opened.Key ? Opened : Joined,
                Enabled = enabled,
                Delivery = delivery,
                IsSet = true
            });

        var teamState = new Mock<ITeamStateService>();
        if (selectedTeam != null)
        {
            var team = new Mock<ITeam>();
            team.SetupGet(x => x.Key).Returns(selectedTeam);
            teamState.Setup(x => x.GetSelectedTeamAsync()).ReturnsAsync(team.Object);
        }

        Services.AddSingleton(_service.Object);
        Services.AddSingleton(teamState.Object);
        Services.AddSingleton<IThargaTextProvider>(new DefaultThargaTextProvider());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;

        return Render<UserNoticeTeamDefaultsView>(p => p.Add(x => x.TeamKey, teamKey));
    }

    private static AngleSharp.Dom.IElement Switch(IRenderedComponent<UserNoticeTeamDefaultsView> view, string notice)
        => view.Find($"[data-notice=\"{notice}\"] .rz-switch");

    [Fact]
    public void TheSelectedTeamsDefaults_AreListed_WithWhatTheTeamChanged()
    {
        var view = Render();

        _service.Verify(x => x.GetTeamDefaultsAsync(TeamKey, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Contains("Notice defaults", view.Markup);
        Assert.Equal(["case.opened", "member.joined"], view.FindAll("[data-notice]").Select(x => x.GetAttribute("data-notice")));
        Assert.NotNull(view.Find("[data-changed=\"case.opened\"]"));
        Assert.Empty(view.FindAll("[data-changed=\"member.joined\"]"));
        Assert.Single(view.FindComponents<RadzenSelectBar<UserNoticeDelivery>>());
    }

    [Fact]
    public async Task TurningADefaultOff_SavesAtOnce_ForTheTeam()
    {
        var view = Render();

        await view.InvokeAsync(() => Switch(view, "member.joined").Click());

        _service.Verify(x => x.SetTeamDefaultAsync(TeamKey, "member.joined", false, UserNoticeDelivery.AtOnce, It.IsAny<CancellationToken>()), Times.Once);
        view.WaitForAssertion(() => Assert.NotNull(view.Find("[data-changed=\"member.joined\"]")));
    }

    [Fact]
    public async Task ChoosingADelivery_SavesAtOnce_ForTheTeam()
    {
        var view = Render();

        var bar = view.FindComponent<RadzenSelectBar<UserNoticeDelivery>>();
        await view.InvokeAsync(() => bar.Instance.Change.InvokeAsync(UserNoticeDelivery.Digest));

        _service.Verify(x => x.SetTeamDefaultAsync(TeamKey, "case.opened", true, UserNoticeDelivery.Digest, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void ATeamNamedByTheHost_IsUsedInsteadOfTheSelectedOne()
    {
        Render(teamKey: "team-9");

        _service.Verify(x => x.GetTeamDefaultsAsync("team-9", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public void AReaderWithoutTheScope_IsToldSo()
    {
        var view = Render(permitted: false);

        Assert.Contains("You cannot change this team's notice defaults.", view.Markup);
        Assert.Empty(view.FindAll("[data-notice]"));
    }

    [Fact]
    public void WithNoTeam_TheReaderIsAskedToSelectOne()
    {
        var view = Render(selectedTeam: null);

        Assert.Contains("Select a team to set its notice defaults.", view.Markup);
        _service.Verify(x => x.GetTeamDefaultsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ASaveThatIsRefused_IsReported_AndTheSwitchShowsWhatIsStillInForce()
    {
        var view = Render();
        _service.Setup(x => x.SetTeamDefaultAsync(TeamKey, "member.joined", false, UserNoticeDelivery.AtOnce, It.IsAny<CancellationToken>())).ThrowsAsync(new UnauthorizedAccessException());

        await view.InvokeAsync(() => Switch(view, "member.joined").Click());

        Assert.Contains(Services.GetRequiredService<NotificationService>().Messages, x => x.Summary == "You cannot change this team's notice defaults." || x.Detail == "You cannot change this team's notice defaults.");
        view.WaitForAssertion(() => Assert.Contains("rz-switch-checked", Switch(view, "member.joined").ClassName));
    }
}
