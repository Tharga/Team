using System.Security.Claims;
using Microsoft.Extensions.Options;
using Tharga.Team.Service;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// Which cases close themselves, and — more importantly — which never do.
/// </summary>
/// <remarks>
/// <b>The direction is the whole feature, and inverting it is the easy mistake.</b> The clock runs while a
/// case waits on the *customer*: support answered and nobody came back. A case whose newest entry is the
/// customer's is waiting on support, and closing that would hide the backlog rather than tidy it — so the
/// three negative tests here matter more than the positive one.
/// </remarks>
public class InactivitySweepTests
{
    private const string TeamA = "team-a";
    private const string Alice = "alice-subject";
    private const string Support = "support-subject";

    private static readonly DateTime Now = new(2026, 9, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ACaseSupportAnsweredLongAgo_ClosesItself()
    {
        var (sweep, store, service) = Build();
        var raised = await service.RaiseCaseAsync(TeamA, null, "The export is empty.");
        await ReplyFromSupportAsync(store, raised.Id, Now.AddDays(-8));

        Assert.Equal(1, await sweep.RunAsync());

        var closed = await store.GetCaseAsync(TeamA, raised.Id);
        Assert.Equal(SupportCaseStatus.Closed, closed.Status);
        Assert.Equal(SupportCaseClosureReason.Inactivity, closed.ClosedReason);
    }

    [Fact]
    public async Task TheClosure_SaysWhyAndThatItCanBeReopened()
    {
        var (sweep, store, service) = Build();
        var raised = await service.RaiseCaseAsync(TeamA, null, "The export is empty.");
        await ReplyFromSupportAsync(store, raised.Id, Now.AddDays(-8));

        await sweep.RunAsync();

        var messages = await store.GetMessagesAsync(TeamA, raised.Id, null, 50);
        var closure = messages.Items[^1];

        Assert.Equal(SupportMessageKind.System, closure.Kind);
        Assert.Contains("automatically", closure.Body);
        Assert.Contains("Reopen", closure.Body);
    }

    /// <summary>
    /// <b>The one that must never fire.</b> This case is the backlog: support has not answered.
    /// </summary>
    [Fact]
    public async Task ACaseWaitingOnSupport_NeverCloses_HoweverOld()
    {
        var (sweep, store, service) = Build();
        var raised = await service.RaiseCaseAsync(TeamA, null, "The export is empty.");
        await ReplyFromAuthorAsync(store, raised.Id, Now.AddYears(-2));

        Assert.Equal(0, await sweep.RunAsync());
        Assert.Equal(SupportCaseStatus.Open, (await store.GetCaseAsync(TeamA, raised.Id)).Status);
    }

    /// <summary>
    /// A reopen note is a system entry. If it started the clock, a reopened case would close itself on the
    /// very next sweep — which is the bug this whole distinction exists to prevent.
    /// </summary>
    [Fact]
    public async Task ACaseWhoseNewestEntryIsTheToolkits_NeverCloses()
    {
        var (sweep, store, service) = Build();
        var raised = await service.RaiseCaseAsync(TeamA, null, "The export is empty.");
        await ReplyFromSupportAsync(store, raised.Id, Now.AddDays(-30));
        await service.CloseCaseAsync(TeamA, raised.Id);

        await store.ReopenCaseAsync(TeamA, raised.Id, new SupportMessage
        {
            Sequence = 0,
            Kind = SupportMessageKind.System,
            Body = "Case reopened.",
            SentAt = Now.AddDays(-20)
        });

        Assert.Equal(0, await sweep.RunAsync());
        Assert.Equal(SupportCaseStatus.Open, (await store.GetCaseAsync(TeamA, raised.Id)).Status);
    }

    [Fact]
    public async Task ACaseSupportAnsweredRecently_DoesNotCloseYet()
    {
        var (sweep, store, service) = Build();
        var raised = await service.RaiseCaseAsync(TeamA, null, "The export is empty.");
        await ReplyFromSupportAsync(store, raised.Id, Now.AddDays(-2));

        Assert.Equal(0, await sweep.RunAsync());
    }

    [Fact]
    public async Task AnAlreadyClosedCase_IsNotClosedAgain()
    {
        var (sweep, store, service) = Build();
        var raised = await service.RaiseCaseAsync(TeamA, null, "The export is empty.");
        await ReplyFromSupportAsync(store, raised.Id, Now.AddDays(-8));
        await service.CloseCaseAsync(TeamA, raised.Id);

        Assert.Equal(0, await sweep.RunAsync());
    }

    /// <summary>
    /// Two instances sweep together. The store's write is conditional, so exactly one closes it.
    /// </summary>
    [Fact]
    public async Task TwoSweepsRunningTogether_CloseItOnce()
    {
        var (first, store, service) = Build();
        var (second, _, _) = Build(store);

        var raised = await service.RaiseCaseAsync(TeamA, null, "The export is empty.");
        await ReplyFromSupportAsync(store, raised.Id, Now.AddDays(-8));

        var closedByFirst = await first.RunAsync();
        var closedBySecond = await second.RunAsync();

        Assert.Equal(1, closedByFirst + closedBySecond);

        var messages = await store.GetMessagesAsync(TeamA, raised.Id, null, 50);
        Assert.Single(messages.Items, x => x.Kind == SupportMessageKind.System);
    }

    [Fact]
    public async Task WithAutoCloseOff_NothingIsSwept()
    {
        var (sweep, store, service) = Build(autoCloseAfter: TimeSpan.Zero);
        var raised = await service.RaiseCaseAsync(TeamA, null, "The export is empty.");
        await ReplyFromSupportAsync(store, raised.Id, Now.AddYears(-1));

        Assert.Equal(0, await sweep.RunAsync());
        Assert.Equal(SupportCaseStatus.Open, (await store.GetCaseAsync(TeamA, raised.Id)).Status);
    }

    [Fact]
    public async Task TheBatchSize_BoundsOneSweep()
    {
        var (sweep, store, service) = Build(batchSize: 2);

        for (var i = 0; i < 5; i++)
        {
            var raised = await service.RaiseCaseAsync(TeamA, null, $"Problem {i}.");
            await ReplyFromSupportAsync(store, raised.Id, Now.AddDays(-8));
        }

        Assert.Equal(2, await sweep.RunAsync());
    }

    [Fact]
    public async Task ClosingAutomatically_NotifiesTheHost_AsNotFromAChannel()
    {
        var notifier = Substitute.For<ISupportCaseNotifier>();
        var (sweep, store, service) = Build(notifier: notifier);
        var raised = await service.RaiseCaseAsync(TeamA, null, "The export is empty.");
        await ReplyFromSupportAsync(store, raised.Id, Now.AddDays(-8));

        await sweep.RunAsync();

        notifier.Received(1).Notify(Arg.Is<SupportCaseUpdatedEventArgs>(x =>
            x.CaseId == raised.Id && x.Change == SupportCaseChange.Closed && !x.FromChannel));
    }

    private static Task ReplyFromSupportAsync(InMemorySupportCaseStore store, string caseId, DateTime at)
        => store.AppendMessageAsync(TeamA, caseId, new SupportMessage
        {
            Sequence = 0,
            Kind = SupportMessageKind.User,
            AuthorIdentity = Support,
            AuthorName = "Support",
            Body = "Have you tried again?",
            SentAt = at
        });

    private static Task ReplyFromAuthorAsync(InMemorySupportCaseStore store, string caseId, DateTime at)
        => store.AppendMessageAsync(TeamA, caseId, new SupportMessage
        {
            Sequence = 0,
            Kind = SupportMessageKind.User,
            AuthorIdentity = Alice,
            AuthorName = "Alice",
            Body = "Still broken.",
            SentAt = at
        });

    private static (SupportCaseInactivitySweep Sweep, InMemorySupportCaseStore Store, ISupportCaseService Service) Build(
        InMemorySupportCaseStore store = null,
        TimeSpan? autoCloseAfter = null,
        int batchSize = 100,
        ISupportCaseNotifier notifier = null)
    {
        store ??= new InMemorySupportCaseStore();

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, Alice),
            new(ClaimTypes.Name, "Alice"),
            new(TeamClaimTypes.TeamKey, TeamA)
        };

        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        var authorizer = new TeamAuthorizer(new FixedPrincipalAccessor(principal));
        var time = new FixedTimeProvider(Now);

        var options = Options.Create(new SupportCaseOptions
        {
            AutoCloseAfter = autoCloseAfter ?? TimeSpan.FromDays(7),
            AutoCloseBatchSize = batchSize
        });

        var service = new AuthorizationSupportCaseServiceDecorator(
            new SupportCaseService(store, authorizer, time), authorizer);

        return (new SupportCaseInactivitySweep(store, options, time, notifier), store, service);
    }

    private sealed class FixedPrincipalAccessor(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }

    private sealed class FixedTimeProvider(DateTime utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(utcNow, TimeSpan.Zero);
    }
}
