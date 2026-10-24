using Blazored.LocalStorage;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Radzen;
using Tharga.Team.Blazor.Features.Support;
using Tharga.Team.Blazor.Framework;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Tests;

/// <summary>Presence as a component meets it: a state to read, and a change to hear.</summary>
internal sealed class TestPresence(SupportPresenceState state = SupportPresenceState.Unknown) : ISupportPresence
{
    public SupportPresenceState State { get; private set; } = state;

    public Task<SupportPresenceState> GetAsync(CancellationToken cancellationToken = default) => Task.FromResult(State);

    public event EventHandler Changed;

    public void Becomes(SupportPresenceState state)
    {
        State = state;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}

/// <summary>
/// When a member writing to a person is told that we will get back to them: worked out from the conversation and the
/// clock, and stored nowhere.
/// </summary>
public class SupportNoAnswerWatchTests
{
    private const string Author = "alice";
    private static readonly DateTime At = new(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc);

    private static SupportMessage Written(int sequence, string author = Author, SupportMessageKind kind = SupportMessageKind.User, bool asSupport = false) => new()
    {
        Sequence = sequence,
        Kind = kind,
        AuthorIdentity = author,
        AuthorName = author,
        Body = $"Entry {sequence}",
        SentAt = At.AddSeconds(sequence),
        AsSupport = asSupport
    };

    [Fact]
    public void TheWait_StartsAtTheMembersFirstMessage_SinceAnybodyElseLastWrote()
    {
        Assert.Equal(At.AddSeconds(3), SupportNoAnswerWatch.WaitingSince([Written(1), Written(2, "staff", asSupport: true), Written(3), Written(4)], Author));
    }

    [Fact]
    public void AnAnswer_EndsTheWait_WhoeverWroteIt()
    {
        Assert.Null(SupportNoAnswerWatch.WaitingSince([Written(1), Written(2, "staff", asSupport: true)], Author));
        Assert.Null(SupportNoAnswerWatch.WaitingSince([Written(1), Written(2, kind: SupportMessageKind.Assistant)], Author));
        Assert.Null(SupportNoAnswerWatch.WaitingSince([Written(1), Written(2, "bob")], Author));
    }

    [Fact]
    public void ANoteAboutTheCase_IsNotAnAnswer()
    {
        Assert.Equal(At.AddSeconds(1), SupportNoAnswerWatch.WaitingSince([Written(1), Written(2, "system", SupportMessageKind.System)], Author));
    }

    [Fact]
    public void TheMemberIsTold_OnceTheWaitRunsOut_AndCalledThen()
    {
        var time = new ManualTime();
        var due = 0;
        using var watch = new SupportNoAnswerWatch(time, () => due++);
        watch.Follow([Written(1)], Author);

        time.Advance(TimeSpan.FromSeconds(60));
        Assert.False(watch.Shows(SupportPresenceState.Online));
        Assert.Equal(0, due);

        time.Advance(TimeSpan.FromSeconds(1));
        Assert.True(watch.Shows(SupportPresenceState.Online));
        Assert.Equal(1, due);
    }

    [Fact]
    public void WhileSupportIsAway_TheMemberIsToldAtOnce()
    {
        using var watch = new SupportNoAnswerWatch(new ManualTime(), null);
        watch.Follow([Written(1)], Author);

        Assert.True(watch.Shows(SupportPresenceState.Away));
        Assert.False(watch.Shows(SupportPresenceState.Unknown));
    }

    [Fact]
    public void TheHost_ChoosesHowLongTheWaitIs()
    {
        var time = new ManualTime();
        using var watch = new SupportNoAnswerWatch(time, null) { After = TimeSpan.FromMinutes(5) };
        watch.Follow([Written(1)], Author);

        time.Advance(TimeSpan.FromMinutes(2));
        Assert.False(watch.Shows(SupportPresenceState.Online));

        time.Advance(TimeSpan.FromMinutes(4));
        Assert.True(watch.Shows(SupportPresenceState.Online));
    }

    [Fact]
    public void ANewConversation_HasNothingWaiting()
    {
        var time = new ManualTime();
        using var watch = new SupportNoAnswerWatch(time, null);
        watch.Follow([Written(1)], Author);

        watch.Clear();
        time.Advance(TimeSpan.FromMinutes(5));

        Assert.False(watch.Shows(SupportPresenceState.Away));
    }
}

/// <summary>
/// The member's conversation with a person on the support page: whether support is online, and the word when nobody
/// answers. Support sees neither.
/// </summary>
public class SupportCaseConversationPresenceTests : BunitContext
{
    private const string TeamKey = "acme";

    private readonly Mock<ISupportCaseService> _cases = new();
    private readonly ManualTime _time = new();

    private static readonly SupportCase Case = new()
    {
        Id = "case-1",
        TeamKey = TeamKey,
        AuthorIdentity = "alice",
        AuthorName = "Alice",
        Subject = "The export",
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 10, 24, 12, 0, 0, DateTimeKind.Utc),
        MessageCount = 1,
        AssistantState = SupportAssistantState.None
    };

    private IRenderedComponent<SupportCaseConversation> Render(bool asStaff, TestPresence presence, SupportCase supportCase = null)
    {
        _cases.Setup(x => x.GetMessagesAsync(TeamKey, Case.Id, It.IsAny<string>(), It.IsAny<int>(), default))
            .ReturnsAsync(new SupportMessagePage
            {
                Items = [new SupportMessage { Sequence = 1, Kind = SupportMessageKind.User, AuthorIdentity = "alice", AuthorName = "Alice", Body = "The export is empty.", SentAt = new DateTime(2026, 10, 24, 12, 0, 1, DateTimeKind.Utc) }]
            });
        Services.AddSingleton(_cases.Object);
        Services.AddSingleton<ISupportPresence>(presence);
        Services.AddSingleton<TimeProvider>(_time);
        Services.AddSingleton(Mock.Of<ISupportCaseNotifier>());
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddSingleton(Mock.Of<IIconResolver>());
        Services.AddSingleton(Mock.Of<ILocalStorageService>());
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("Server", true));

        return Render<SupportCaseConversation>(p => p.Add(x => x.Case, supportCase ?? Case).Add(x => x.AsStaff, asStaff));
    }

    [Fact]
    public void TheMember_SeesWhetherSupportIsOnline_AndIsToldAfterAMinute()
    {
        var view = Render(asStaff: false, new TestPresence(SupportPresenceState.Online));

        Assert.NotNull(view.Find("[data-presence='Online']"));
        Assert.Empty(view.FindAll("[data-no-answer]"));

        _time.Advance(TimeSpan.FromSeconds(61));

        view.WaitForAssertion(() => Assert.Equal(SupportTranscriptText.NoAnswerYet.Default, view.Find("[data-no-answer] [data-notice]").TextContent.Trim()));
    }

    [Fact]
    public void WhileSupportIsAway_TheMemberIsToldAtOnce()
    {
        var view = Render(asStaff: false, new TestPresence(SupportPresenceState.Away));

        Assert.NotNull(view.Find("[data-presence='Away']"));
        Assert.Equal(SupportTranscriptText.NoAnswerAway.Default, view.Find("[data-no-answer] [data-notice]").TextContent.Trim());
    }

    [Fact]
    public void Support_SeesNeither()
    {
        var view = Render(asStaff: true, new TestPresence(SupportPresenceState.Away));

        _time.Advance(TimeSpan.FromMinutes(2));

        Assert.Empty(view.FindAll("[data-presence]"));
        Assert.Empty(view.FindAll("[data-no-answer]"));
    }

    [Fact]
    public void WithTheAssistant_NothingIsSaidAboutSupport()
    {
        var view = Render(asStaff: false, new TestPresence(SupportPresenceState.Away), Case with { AssistantState = SupportAssistantState.Active });

        Assert.Empty(view.FindAll("[data-presence]"));
        Assert.Empty(view.FindAll("[data-no-answer]"));
    }
}

/// <summary>
/// How a support person is shown to members, chosen on the desk: what it starts as, what choosing does, and who sees
/// it at all.
/// </summary>
public class SupportDeskStatusTests : BunitContext
{
    private const string StorageKey = "Tharga.Team.Support.DeskStatus";
    private const string ModulePath = "./_content/Tharga.Team.Blazor/support-desk.js";

    private readonly Mock<ISupportCaseService> _cases = new();
    private readonly Mock<ILocalStorageService> _storage = new();

    private IRenderedComponent<SupportDeskStatus> Render(SupportStaffStatus? set = null, SupportStaffStatus? remembered = null, bool staff = true, ISupportPresence presence = null)
    {
        if (staff) _cases.Setup(x => x.GetMyStaffStatusAsync(default)).ReturnsAsync(set);
        else _cases.Setup(x => x.GetMyStaffStatusAsync(default)).ThrowsAsync(new UnauthorizedAccessException());
        _storage.Setup(x => x.GetItemAsync<SupportStaffStatus?>(StorageKey, It.IsAny<CancellationToken>())).ReturnsAsync(remembered);

        Services.AddSingleton(_cases.Object);
        Services.AddSingleton(_storage.Object);
        if (presence != null) Services.AddSingleton(presence);
        Services.AddSingleton<IThargaTextProvider, DefaultThargaTextProvider>();
        Services.AddRadzenComponents();
        JSInterop.Mode = JSRuntimeMode.Loose;
        SetRendererInfo(new RendererInfo("Server", true));

        return Render<SupportDeskStatus>();
    }

    private static string Pressed(IRenderedComponent<SupportDeskStatus> view)
        => view.FindAll("[data-status-choice]").Single(x => x.GetAttribute("aria-pressed") == "true").GetAttribute("data-status-choice");

    [Fact]
    public void SomebodyNotOnTheDesk_SeesNothing_AndIsNotPutOnIt()
    {
        var view = Render(staff: false);

        Assert.Empty(view.FindAll("[data-desk-status]"));
        _cases.Verify(x => x.SetMyStaffStatusAsync(It.IsAny<SupportStaffStatus>(), default), Times.Never);
    }

    [Fact]
    public void OpeningTheDesk_TheFirstTime_PutsThemOnline()
    {
        var view = Render();

        _cases.Verify(x => x.SetMyStaffStatusAsync(SupportStaffStatus.Online, default), Times.Once);
        Assert.Equal("Online", Pressed(view));
        Assert.Equal(SupportDeskStatusText.OnlineHint.Default, view.Find("[data-status-hint]").TextContent.Trim());
    }

    [Fact]
    public void TheirChoiceLastTime_InThisBrowser_IsKept()
    {
        var view = Render(remembered: SupportStaffStatus.Hidden);

        _cases.Verify(x => x.SetMyStaffStatusAsync(SupportStaffStatus.Hidden, default), Times.Once);
        Assert.Equal("Hidden", Pressed(view));
    }

    [Fact]
    public void WhatAnotherPageOfTheirsSet_WinsOverTheBrowser()
    {
        var view = Render(set: SupportStaffStatus.Away, remembered: SupportStaffStatus.Hidden);

        _cases.Verify(x => x.SetMyStaffStatusAsync(SupportStaffStatus.Away, default), Times.Once);
        Assert.Equal("Away", Pressed(view));
    }

    [Fact]
    public void Choosing_SetsIt_AndRemembersIt()
    {
        var view = Render();

        view.Find("[data-status-choice='Away']").Click();

        _cases.Verify(x => x.SetMyStaffStatusAsync(SupportStaffStatus.Away, default), Times.Once);
        _storage.Verify(x => x.SetItemAsync(StorageKey, SupportStaffStatus.Away, It.IsAny<CancellationToken>()), Times.Once);
        Assert.Equal("Away", Pressed(view));
    }

    [Fact]
    public async Task UsingThePage_IsWatched_AndReported()
    {
        var module = JSInterop.SetupModule(ModulePath);
        module.SetupVoid("watchActivity", _ => true);
        var view = Render();

        view.WaitForAssertion(() => Assert.Single(module.Invocations["watchActivity"]));
        await view.InvokeAsync(() => view.Instance.OnActivity());

        _cases.Verify(x => x.ReportStaffActivityAsync(default), Times.Once);
    }

    [Fact]
    public void AChoiceOnAnotherPage_Shows()
    {
        var presence = new TestPresence();
        var view = Render(presence: presence);

        _cases.Setup(x => x.GetMyStaffStatusAsync(default)).ReturnsAsync(SupportStaffStatus.Away);
        presence.Becomes(SupportPresenceState.Away);

        view.WaitForAssertion(() => Assert.Equal("Away", Pressed(view)));
    }
}
