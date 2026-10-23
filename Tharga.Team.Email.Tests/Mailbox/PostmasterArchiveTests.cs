using System.Security.Claims;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Mailbox;
using Tharga.Team.Email.Tests.Users;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Mailbox;

/// <summary>
/// Putting a mail away without routing it, and bringing it back: what staff do with spam, a mail sent by mistake or
/// one already dealt with, so the postmaster's list holds only what still needs a team.
/// </summary>
public class PostmasterArchiveTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private readonly InMemoryTeamMailStore _mail = new();
    private readonly InMemoryContentStore _content = new();
    private readonly TeamMailNotifier _notifier = new();
    private readonly List<TeamMailUpdatedEventArgs> _notified = [];
    private readonly ITeamService _teams = Substitute.For<ITeamService>();

    public PostmasterArchiveTests()
    {
        _notifier.MailUpdated += (_, e) => _notified.Add(e);
        _teams.GetTeamByKeyAsync(Arg.Any<string>()).Returns((ITeam)null);
        _teams.GetTeamByKeyAsync("team-a").Returns(Substitute.For<ITeam>());
    }

    private PostmasterService Postmaster(ClaimsPrincipal caller = null) =>
        new(_mail, _notifier, _content, _teams, new FixedPrincipal(caller ?? Staff()), new SteppingTimeProvider(Now));

    private static ClaimsPrincipal Staff(params Claim[] extra)
        => new(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Sam Staff"), new Claim(ClaimTypes.NameIdentifier, "sam"), .. extra], "Test"));

    private async Task KeepAsync(string id, string teamKey = null, DateTime? receivedAt = null)
    {
        await _mail.AddAsync(new InboundMailItem
        {
            Id = id,
            TeamKey = teamKey,
            Envelope = new MailEnvelope { MessageId = $"<{id}@example.org>", From = "anna@example.org", DeliveredTo = "oldkey-mail@example.net", Subject = $"About {id}" },
            Reason = teamKey == null ? MailRouteReason.UnknownAddress : MailRouteReason.PrimaryKey,
            ReceivedAt = receivedAt ?? Now
        });
        await _content.SaveAsync(id, [7]);
    }

    [Fact]
    public async Task Archiving_RecordsWhoAndWhen_UnderTheArchivedOutcome()
    {
        await KeepAsync("mail-1");

        var archived = await Postmaster().ArchiveAsync("mail-1");

        Assert.Equal(PostmasterMailOutcome.Archived, archived.Outcome);
        Assert.Equal("archived", PostmasterMailOutcome.Archived);
        Assert.Equal(Now, archived.HandledAt);
        Assert.Equal("Sam Staff", archived.HandledBy);
        Assert.Null(archived.TeamKey);
    }

    [Fact]
    public async Task AnArchivedMail_LeavesTheHeldListAndTheCount_AndIsListedAsArchived()
    {
        await KeepAsync("mail-1");
        await KeepAsync("mail-2");

        await Postmaster().ArchiveAsync("mail-1");

        Assert.Equal(1, await Postmaster().GetCountAsync());
        Assert.Equal(["mail-2"], (await Postmaster().ListAsync()).Items.Select(x => x.Id));
        Assert.Equal(["mail-2"], (await Postmaster().ListAsync(PostmasterMailState.Held)).Items.Select(x => x.Id));

        var summary = Assert.Single((await Postmaster().ListAsync(PostmasterMailState.Archived)).Items);
        Assert.Equal("mail-1", summary.Id);
        Assert.Equal(Now, summary.ArchivedAt);
        Assert.Equal("Sam Staff", summary.ArchivedBy);
    }

    [Fact]
    public async Task AHeldMail_SaysNothingOfArchiving()
    {
        await KeepAsync("mail-1");

        var summary = Assert.Single((await Postmaster().ListAsync()).Items);

        Assert.Null(summary.ArchivedAt);
        Assert.Null(summary.ArchivedBy);
    }

    [Fact]
    public async Task ArchivedMail_IsListedNewestReceivedFirst_WhateverOrderItWasArchivedIn()
    {
        await KeepAsync("older", receivedAt: Now.AddDays(-3));
        await KeepAsync("newer", receivedAt: Now.AddDays(-1));

        await Postmaster().ArchiveAsync("newer");
        await Postmaster().ArchiveAsync("older");

        Assert.Equal(["newer", "older"], (await Postmaster().ListAsync(PostmasterMailState.Archived)).Items.Select(x => x.Id));
    }

    [Fact]
    public async Task Archiving_IsAnnounced_WithNoTeam()
    {
        await KeepAsync("mail-1");

        await Postmaster().ArchiveAsync("mail-1");

        var change = Assert.Single(_notified);
        Assert.Equal(TeamMailChange.Archived, change.Change);
        Assert.Null(change.TeamKey);
        Assert.True(change.AffectsPostmaster);
        Assert.Equal("mail-1", change.MailId);
    }

    [Fact]
    public async Task ArchivingAnArchivedMail_ReturnsItUnchanged_AndAnnouncesNothing()
    {
        await KeepAsync("mail-1");
        var first = await Postmaster().ArchiveAsync("mail-1");
        _notified.Clear();

        var someoneElse = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Someone Else")], "Test"));

        var again = await Postmaster(someoneElse).ArchiveAsync("mail-1");

        Assert.Equal(first, again);
        Assert.Equal("Sam Staff", again.HandledBy);
        Assert.Empty(_notified);
    }

    [Fact]
    public async Task Restoring_BringsTheMailBack_UnhandledAgain_AndAnnouncesIt()
    {
        await KeepAsync("mail-1");
        await Postmaster().ArchiveAsync("mail-1");
        _notified.Clear();

        var restored = await Postmaster().RestoreAsync("mail-1");

        Assert.Null(restored.Outcome);
        Assert.Null(restored.HandledAt);
        Assert.Null(restored.HandledBy);
        Assert.Equal(1, await Postmaster().GetCountAsync());
        Assert.Equal(["mail-1"], (await Postmaster().ListAsync()).Items.Select(x => x.Id));
        Assert.Empty((await Postmaster().ListAsync(PostmasterMailState.Archived)).Items);
        var change = Assert.Single(_notified);
        Assert.Equal(TeamMailChange.Restored, change.Change);
        Assert.Null(change.TeamKey);
        Assert.True(change.AffectsPostmaster);
    }

    [Fact]
    public async Task RestoringAHeldMail_ReturnsItUnchanged_AndAnnouncesNothing()
    {
        await KeepAsync("mail-1");

        var restored = await Postmaster().RestoreAsync("mail-1");

        Assert.Equal("mail-1", restored.Id);
        Assert.Null(restored.Outcome);
        Assert.Empty(_notified);
    }

    [Fact]
    public async Task AMailRoutedToATeam_CanNeitherBeArchivedNorRestored()
    {
        await KeepAsync("mail-1");
        await Postmaster().RouteAsync("mail-1", "team-a");
        _notified.Clear();

        Assert.Null(await Postmaster().ArchiveAsync("mail-1"));
        Assert.Null(await Postmaster().RestoreAsync("mail-1"));

        var mail = Assert.Single(_mail.All);
        Assert.Equal("team-a", mail.TeamKey);
        Assert.Null(mail.Outcome);
        Assert.Empty(_notified);
    }

    [Fact]
    public async Task ATeamsOwnMail_IsOutOfReach()
    {
        await KeepAsync("mail-1", "team-a");

        Assert.Null(await Postmaster().ArchiveAsync("mail-1"));
        Assert.Null(Assert.Single(_mail.All).Outcome);
    }

    [Fact]
    public async Task AMailThatDoesNotExist_IsNeitherArchivedNorRestored()
    {
        Assert.Null(await Postmaster().ArchiveAsync("no-such-mail"));
        Assert.Null(await Postmaster().RestoreAsync("no-such-mail"));
        Assert.Empty(_notified);
    }

    [Fact]
    public async Task AnArchivedMail_CanStillBeOpened()
    {
        await KeepAsync("mail-1");
        await Postmaster().ArchiveAsync("mail-1");

        Assert.Equal(PostmasterMailOutcome.Archived, (await Postmaster().OpenAsync("mail-1")).Outcome);
        Assert.Equal([7], (await Postmaster().GetContentAsync("mail-1")).Raw);
    }

    [Fact]
    public async Task RoutingAnArchivedMail_DeliversItToTheTeamUnhandled()
    {
        await KeepAsync("mail-1");
        await Postmaster().ArchiveAsync("mail-1");

        var routed = await Postmaster().RouteAsync("mail-1", "team-a");

        Assert.Equal("team-a", routed.TeamKey);
        Assert.Null(routed.Outcome);
        Assert.Null(routed.HandledBy);
        Assert.Empty((await Postmaster().ListAsync(PostmasterMailState.Archived)).Items);
    }

    [Fact]
    public async Task TheRetentionSweep_DeletesArchivedMail_LikeTheRest()
    {
        await KeepAsync("old", receivedAt: Now.AddDays(-91));
        await KeepAsync("recent", receivedAt: Now.AddDays(-1));
        await Postmaster().ArchiveAsync("old");
        await Postmaster().ArchiveAsync("recent");

        var deleted = await _mail.DeleteUnroutedReceivedBeforeAsync(Now.AddDays(-90));

        Assert.Equal(["old"], deleted);
        Assert.Equal(["recent"], (await Postmaster().ListAsync(PostmasterMailState.Archived)).Items.Select(x => x.Id));
    }

    [Fact]
    public async Task ACallerTheHostCannotName_ArchivesWithNoName()
    {
        await KeepAsync("mail-1");

        var archived = await new PostmasterService(_mail, _notifier, _content, _teams, null, new SteppingTimeProvider(Now)).ArchiveAsync("mail-1");

        Assert.Equal(PostmasterMailOutcome.Archived, archived.Outcome);
        Assert.Null(archived.HandledBy);
    }

    [Fact]
    public async Task AStoreThatDoesNotArchive_SaysSo_RatherThanListingNothing()
    {
        var store = Substitute.For<ITeamMailStore>();
        store.ListUnroutedAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(new InboundMailPage { Items = [] });
        ITeamMailStore plain = new PlainStore(store);

        await plain.ListUnroutedAsync(PostmasterMailState.Held, null, 10);

        await store.Received(1).ListUnroutedAsync(null, 10, Arg.Any<CancellationToken>());
        await Assert.ThrowsAsync<NotSupportedException>(() => plain.ListUnroutedAsync(PostmasterMailState.Archived, null, 10));
        await Assert.ThrowsAsync<NotSupportedException>(() => plain.ArchiveUnroutedAsync("mail-1", "Sam", Now));
        await Assert.ThrowsAsync<NotSupportedException>(() => plain.RestoreUnroutedAsync("mail-1"));
    }

    [Fact]
    public async Task ThroughTheProxy_ManagingArchivesAndRestores_EachRecordedAsAChange()
    {
        await KeepAsync("mail-1");
        var audit = new RecordingAuditLogger();
        var postmaster = Resolve(Staff(new Claim(TeamClaimTypes.SystemScope, SystemMailboxScopes.PostmasterManage)), audit);

        Assert.NotNull(await postmaster.ArchiveAsync("mail-1"));
        Assert.NotNull(await postmaster.RestoreAsync("mail-1"));

        Assert.Equal([nameof(IPostmasterService.ArchiveAsync), nameof(IPostmasterService.RestoreAsync)], audit.Entries.Select(x => x.MethodName));
        Assert.All(audit.Entries, x => Assert.Equal(AuditEventType.DataChange, x.EventType));
        Assert.All(audit.Entries, x => Assert.Equal(SystemMailboxScopes.PostmasterManage, x.ScopeChecked));
    }

    [Fact]
    public async Task ThroughTheProxy_ReadingAloneArchivesNothing_AndTheRefusalIsRecorded()
    {
        await KeepAsync("mail-1");
        var audit = new RecordingAuditLogger();
        var postmaster = Resolve(Staff(new Claim(TeamClaimTypes.SystemScope, SystemMailboxScopes.PostmasterRead)), audit);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => postmaster.ArchiveAsync("mail-1"));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => postmaster.RestoreAsync("mail-1"));

        Assert.Null(Assert.Single(_mail.All).Outcome);
        Assert.Equal(2, audit.Entries.Count(x => x.EventType == AuditEventType.ScopeDenial));
    }

    [Fact]
    public async Task ThroughTheProxy_TheSameNameAsATeamScope_GrantsNothing()
    {
        await KeepAsync("mail-1");
        var postmaster = Resolve(Staff(new Claim(TeamClaimTypes.TeamKey, "team-a"), new Claim(TeamClaimTypes.Scope, SystemMailboxScopes.PostmasterManage)), new RecordingAuditLogger());

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => postmaster.ArchiveAsync("mail-1"));
    }

    [Fact]
    public async Task ThroughTheProxy_ListingByStateNeedsOnlyTheReadScope()
    {
        await KeepAsync("mail-1");
        await Postmaster().ArchiveAsync("mail-1");
        var postmaster = Resolve(Staff(new Claim(TeamClaimTypes.SystemScope, SystemMailboxScopes.PostmasterRead)), new RecordingAuditLogger());

        Assert.Single((await postmaster.ListAsync(PostmasterMailState.Archived)).Items);
        Assert.Equal(0, await postmaster.GetCountAsync());
    }

    private IPostmasterService Resolve(ClaimsPrincipal caller, RecordingAuditLogger audit)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<ITeamPrincipalAccessor>(new FixedPrincipal(caller));
        services.AddSingleton<ITeamMailKeyStore>(new InMemoryTeamMailKeyStore());
        services.AddSingleton<ITeamMailStore>(_mail);
        services.AddSingleton<IInboundMailContentStore>(_content);
        services.AddSingleton<TimeProvider>(new SteppingTimeProvider(Now));
        services.AddSingleton(new CompositeAuditLogger([audit], Options.Create(new AuditOptions())));
        services.AddThargaTeamMailbox(o => o.Domain = "example.net");

        return services.BuildServiceProvider().CreateScope().ServiceProvider.GetRequiredService<IPostmasterService>();
    }

    private sealed class FixedPrincipal(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }

    /// <summary>A host's own store written before archiving existed: only the members it always had.</summary>
    private sealed class PlainStore(ITeamMailStore inner) : ITeamMailStore
    {
        public Task<bool> AddAsync(InboundMailItem item, CancellationToken cancellationToken = default) => inner.AddAsync(item, cancellationToken);
        public Task<InboundMailItem> GetAsync(string teamKey, string mailId, CancellationToken cancellationToken = default) => inner.GetAsync(teamKey, mailId, cancellationToken);
        public Task<InboundMailPage> ListAsync(string teamKey, InboundMailFilter filter, string cursor, int pageSize, CancellationToken cancellationToken = default) => inner.ListAsync(teamKey, filter, cursor, pageSize, cancellationToken);
        public Task<int> CountUnhandledAsync(string teamKey, CancellationToken cancellationToken = default) => inner.CountUnhandledAsync(teamKey, cancellationToken);
        public Task<InboundMailItem> SetOutcomeAsync(string teamKey, string mailId, string outcome, MailThreadOwner owner, string handledBy, DateTime handledAt, CancellationToken cancellationToken = default) => inner.SetOutcomeAsync(teamKey, mailId, outcome, owner, handledBy, handledAt, cancellationToken);
        public Task<InboundMailItem> GetUnroutedAsync(string mailId, CancellationToken cancellationToken = default) => inner.GetUnroutedAsync(mailId, cancellationToken);
        public Task<InboundMailPage> ListUnroutedAsync(string cursor, int pageSize, CancellationToken cancellationToken = default) => inner.ListUnroutedAsync(cursor, pageSize, cancellationToken);
        public Task<int> CountUnroutedAsync(CancellationToken cancellationToken = default) => inner.CountUnroutedAsync(cancellationToken);
        public Task<InboundMailItem> RouteAsync(string mailId, string teamKey, CancellationToken cancellationToken = default) => inner.RouteAsync(mailId, teamKey, cancellationToken);
        public Task<string[]> DeleteUnroutedReceivedBeforeAsync(DateTime receivedBefore, CancellationToken cancellationToken = default) => inner.DeleteUnroutedReceivedBeforeAsync(receivedBefore, cancellationToken);
        public Task<string[]> DeleteForTeamAsync(string teamKey, CancellationToken cancellationToken = default) => inner.DeleteForTeamAsync(teamKey, cancellationToken);
    }
}
