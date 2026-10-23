using System.Security.Claims;
using Microsoft.Extensions.Options;
using Tharga.Team.Email.Users;
using Tharga.Team.Service;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>
/// The global mail log: every mail to users and contacts, of every kind, newest first and narrowed by a filter, read by staff
/// holding users:manage and nobody else, audited, naming who each mail was for.
/// </summary>
public class UserMailLogTests
{
    private const string Other = "user-2";
    private const string Acme = "acme";
    private const string Globex = "globex";

    private readonly UserMailFixture _mail = new();

    public UserMailLogTests()
    {
        _mail.Recipients.Add(new UserMailRecipient { Key = Other, EMail = "bo@example.org", Name = "Bo Berg" });
    }

    private static Claim Manage => new(TeamClaimTypes.SystemScope, SystemUserScopes.Manage);

    private UserMailHistoryService Service(params Claim[] claims)
    {
        var users = Substitute.For<IUserService>();
        var user = Substitute.For<IUser>();
        user.Key.Returns(UserMailFixture.UserKey);
        users.GetCurrentUserAsync(Arg.Any<ClaimsPrincipal>()).Returns(Task.FromResult(user));

        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "Sam"), .. claims], "Test"));

        return new UserMailHistoryService(
            _mail.Queue,
            _mail.Recipients,
            new TeamAuthorizer(new FixedPrincipal(principal)),
            users,
            new CompositeAuditLogger([_mail.Audit], Options.Create(new AuditOptions())),
            new AuditEntryFactory(null));
    }

    /// <summary>
    /// Six mails, a minute apart, oldest first: a mail, a message to two users about one team, a notice about another, an
    /// invitation to an address alone about the first team, and a message to a contact.
    /// </summary>
    private async Task SeedAsync()
    {
        await _mail.Queue.QueueAsync(new UserMail { RecipientUserKey = UserMailFixture.UserKey, Category = UserMailFixture.News, Subject = "Welcome", Body = "Hello." });
        _mail.Time.Advance(TimeSpan.FromMinutes(1));
        await _mail.Queue.QueueBatchAsync(new UserMailBatch { RecipientUserKeys = [UserMailFixture.UserKey, Other], Category = UserMailCategories.ServiceKey, Subject = "Maintenance", Body = "On Sunday.", TeamKey = Acme });
        _mail.Time.Advance(TimeSpan.FromMinutes(1));
        await _mail.Queue.QueueAsync(new UserMail { RecipientUserKey = Other, Category = UserMailFixture.News, Subject = "Case opened", Body = "A case.", Kind = UserMailKind.Notice, TeamKey = Globex });
        _mail.Time.Advance(TimeSpan.FromMinutes(1));
        await _mail.Queue.QueueAsync(new UserMail { RecipientAddress = "Bea@Example.org", RecipientName = "Bea Berg", Category = UserMailCategories.AccountKey, Subject = "Invitation", Body = "Join.", Kind = UserMailKind.Invitation, TeamKey = Acme });
        _mail.Time.Advance(TimeSpan.FromMinutes(1));
        await _mail.Outbox.AddBatchAsync(new UserMailBatch
        {
            RecipientUserKeys = [],
            RecipientAddresses = [new UserMailBatchAddress("cora@example.org", "Cora Carlsson")],
            Category = UserMailCategories.ServiceKey,
            Subject = "Newsletter",
            Body = "News.",
            Kind = UserMailKind.Contact
        }, _mail.Time.GetUtcNow().UtcDateTime);
    }

    private async Task<string[]> SubjectsAsync(UserMailFilter filter)
        => [.. (await Service(Manage).ListAllAsync(filter, cancellationToken: TestContext.Current.CancellationToken)).Items.Select(x => x.Subject)];

    [Fact]
    public async Task StaffSeeEveryMail_NewestFirst_OfEveryKind_ContactMailIncluded()
    {
        await SeedAsync();

        var page = await Service(Manage).ListAllAsync(cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["Newsletter", "Invitation", "Case opened", "Maintenance", "Maintenance", "Welcome"], page.Items.Select(x => x.Subject));
        Assert.Equal([UserMailKind.Contact, UserMailKind.Invitation, UserMailKind.Notice, UserMailKind.Message, UserMailKind.Message, UserMailKind.Mail], page.Items.Select(x => x.Kind));
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public async Task EachMail_SaysItsCategory_ResultAndWhenItWasQueued()
    {
        await SeedAsync();

        var invitation = (await Service(Manage).ListAllAsync(new UserMailFilter { Kind = UserMailKind.Invitation }, cancellationToken: TestContext.Current.CancellationToken)).Items.Single();

        Assert.Equal(UserMailCategories.AccountKey, invitation.Category);
        Assert.Equal(UserMailStatus.Queued, invitation.Status);
        Assert.Equal(UserMailFixture.Start.AddMinutes(3), invitation.QueuedAt);
    }

    [Fact]
    public async Task TheResult_IsWhatBecameOfEachMail_SentAndTurnedOff()
    {
        await _mail.QueueAsync();
        await _mail.QueueAsync(UserMailFixture.Digest);
        _mail.Time.Advance(TimeSpan.FromMinutes(1));
        await _mail.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken);

        var sent = await SubjectsAsync(new UserMailFilter { Status = UserMailStatus.Sent });
        var turnedOff = (await Service(Manage).ListAllAsync(new UserMailFilter { Status = UserMailStatus.Skipped }, cancellationToken: TestContext.Current.CancellationToken)).Items.Single();

        Assert.Single(sent);
        Assert.Equal(UserMailFixture.Digest, turnedOff.Category);
        Assert.False(string.IsNullOrWhiteSpace(turnedOff.Reason));
    }

    [Fact]
    public async Task TheKind_NarrowsTheList()
    {
        await SeedAsync();

        Assert.Equal(["Maintenance", "Maintenance"], await SubjectsAsync(new UserMailFilter { Kind = UserMailKind.Message }));
        Assert.Equal(["Newsletter"], await SubjectsAsync(new UserMailFilter { Kind = UserMailKind.Contact }));
        Assert.Equal(["Invitation"], await SubjectsAsync(new UserMailFilter { Kind = UserMailKind.Invitation }));
        Assert.Equal(["Welcome"], await SubjectsAsync(new UserMailFilter { Kind = UserMailKind.Mail }));
    }

    [Fact]
    public async Task TheCategory_NarrowsTheList()
    {
        await SeedAsync();

        Assert.Equal(["Case opened", "Welcome"], await SubjectsAsync(new UserMailFilter { Category = UserMailFixture.News }));
        Assert.Equal(["Invitation"], await SubjectsAsync(new UserMailFilter { Category = UserMailCategories.AccountKey }));
    }

    [Fact]
    public async Task TheDates_NarrowTheList_FromIncludedAndToExcluded()
    {
        await SeedAsync();
        var third = UserMailFixture.Start.AddMinutes(2);

        Assert.Equal(["Newsletter", "Invitation", "Case opened"], await SubjectsAsync(new UserMailFilter { From = third }));
        Assert.Equal(["Maintenance", "Maintenance", "Welcome"], await SubjectsAsync(new UserMailFilter { To = third }));
        Assert.Equal(["Maintenance", "Maintenance"], await SubjectsAsync(new UserMailFilter { From = UserMailFixture.Start.AddMinutes(1), To = third }));
    }

    [Fact]
    public async Task TheRecipient_NarrowsTheList_ByUserKey_OrByAddressAnyCase()
    {
        await SeedAsync();

        Assert.Equal(["Case opened", "Maintenance"], await SubjectsAsync(new UserMailFilter { RecipientUserKey = Other }));
        Assert.Equal(["Invitation"], await SubjectsAsync(new UserMailFilter { RecipientAddress = "  BEA@example.ORG " }));
    }

    [Fact]
    public async Task TheTeam_NarrowsTheList_ToTheMailThatRecordsIt()
    {
        await SeedAsync();

        Assert.Equal(["Invitation", "Maintenance", "Maintenance"], await SubjectsAsync(new UserMailFilter { TeamKey = Acme }));
        Assert.Equal(["Case opened"], await SubjectsAsync(new UserMailFilter { TeamKey = Globex }));
        Assert.Empty(await SubjectsAsync(new UserMailFilter { TeamKey = "nobody" }));
    }

    [Fact]
    public async Task EveryPartGiven_NarrowsFurther()
    {
        await SeedAsync();

        var found = await SubjectsAsync(new UserMailFilter { Kind = UserMailKind.Message, TeamKey = Acme, RecipientUserKey = Other });

        Assert.Equal(["Maintenance"], found);
    }

    [Fact]
    public async Task EachMail_NamesWhoItWasFor_AndTheTeamItIsAbout()
    {
        await SeedAsync();

        var items = (await Service(Manage).ListAllAsync(cancellationToken: TestContext.Current.CancellationToken)).Items;

        var contact = items.Single(x => x.Subject == "Newsletter");
        Assert.Null(contact.RecipientUserKey);
        Assert.Equal("Cora Carlsson", contact.RecipientName);
        Assert.Null(contact.TeamKey);

        var invitation = items.Single(x => x.Subject == "Invitation");
        Assert.Null(invitation.RecipientUserKey);
        Assert.Equal("Bea Berg", invitation.RecipientName);
        Assert.Equal("bea@example.org", invitation.Address);
        Assert.Equal(Acme, invitation.TeamKey);

        var notice = items.Single(x => x.Subject == "Case opened");
        Assert.Equal(Other, notice.RecipientUserKey);
        Assert.Equal("Bo Berg", notice.RecipientName);
        Assert.Equal(Globex, notice.TeamKey);
    }

    [Fact]
    public async Task AUserTheDirectoryNoLongerKnows_IsStillNamedByKey()
    {
        await _mail.Queue.QueueAsync(new UserMail { RecipientUserKey = "gone", Category = UserMailFixture.News, Subject = "Hello", Body = "Hi." });

        var item = (await Service(Manage).ListAllAsync(cancellationToken: TestContext.Current.CancellationToken)).Items.Single();

        Assert.Equal("gone", item.RecipientUserKey);
        Assert.Null(item.RecipientName);
    }

    [Fact]
    public async Task ThePages_FollowTheCursor_WithNothingRepeatedOrMissed()
    {
        await SeedAsync();
        var service = Service(Manage);

        var first = await service.ListAllAsync(null, null, 4, TestContext.Current.CancellationToken);
        var second = await service.ListAllAsync(null, first.NextCursor, 4, TestContext.Current.CancellationToken);

        Assert.Equal(4, first.Items.Length);
        Assert.NotNull(first.NextCursor);
        Assert.Equal(2, second.Items.Length);
        Assert.Null(second.NextCursor);
        Assert.Equal(6, first.Items.Concat(second.Items).Select(x => x.Id).Distinct().Count());
    }

    [Fact]
    public async Task ACursorTheListDidNotIssue_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Service(Manage).ListAllAsync(null, "not-a-cursor", 10, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ACallerWithoutTheScope_IsRefused_AndTheRefusalIsAudited()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service().ListAllAsync(cancellationToken: TestContext.Current.CancellationToken));

        var entry = Assert.Single(_mail.Audit.Entries);
        Assert.Equal(UserMailAudit.Feature, entry.Feature);
        Assert.Equal(UserMailAudit.LogAction, entry.Action);
        Assert.False(entry.Success);
    }

    [Fact]
    public async Task AUserWhoIsInTheLog_StillCannotReadIt()
    {
        await SeedAsync();

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => Service().ListAllAsync(new UserMailFilter { RecipientUserKey = UserMailFixture.UserKey }, cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task StaffReading_IsAudited_WithHowManyAndNeverAnAddress()
    {
        await SeedAsync();

        await Service(Manage).ListAllAsync(new UserMailFilter { RecipientAddress = "bea@example.org" }, cancellationToken: TestContext.Current.CancellationToken);

        var entry = Assert.Single(_mail.Audit.Entries);
        Assert.Equal(UserMailAudit.LogAction, entry.Action);
        Assert.True(entry.Success);
        Assert.Equal("1", entry.Metadata[UserMailAudit.Count]);
        Assert.DoesNotContain(entry.Metadata.Values, x => x.Contains("bea", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TheFilter_IsTrimmed_AndAnAddressIsInTheCaseTheQueueStoresIt()
    {
        var filter = UserMailQueue.Normalized(new UserMailFilter { Category = " news ", RecipientUserKey = " user-1 ", RecipientAddress = " Anna@Example.ORG ", TeamKey = " acme " });

        Assert.Equal(("news", "user-1", "anna@example.org", "acme"), (filter.Category, filter.RecipientUserKey, filter.RecipientAddress, filter.TeamKey));
    }

    [Fact]
    public void ABlankFilter_IsAnEmptyOne()
    {
        var filter = UserMailQueue.Normalized(new UserMailFilter { Category = " ", RecipientUserKey = "", RecipientAddress = "  ", TeamKey = null });

        Assert.Equal(new UserMailFilter(), filter);
        Assert.Equal(new UserMailFilter(), UserMailQueue.Normalized(null));
    }

    private sealed class FixedPrincipal(ClaimsPrincipal principal) : ITeamPrincipalAccessor
    {
        public ValueTask<ClaimsPrincipal> GetCurrentAsync() => ValueTask.FromResult(principal);
    }
}
