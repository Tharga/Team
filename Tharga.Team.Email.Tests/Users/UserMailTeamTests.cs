using Tharga.Team.Email.Users;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>
/// The team a mail is about, recorded so the mail log can be filtered by organisation: set for an invitation, for a notice and
/// for a message that is about one team, and left out where a mail belongs to none or to several.
/// </summary>
public class UserMailTeamTests
{
    private readonly UserMailFixture _mail = new();

    [Fact]
    public async Task AMailQueued_KeepsItsTeam_Trimmed()
    {
        await _mail.Queue.QueueAsync(new UserMail { RecipientUserKey = UserMailFixture.UserKey, Category = UserMailFixture.News, Subject = "S", Body = "B", TeamKey = "  acme " });

        Assert.Equal("acme", _mail.Outbox.Single.Mail.TeamKey);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AMailWithNoTeam_KeepsNone(string teamKey)
    {
        await _mail.Queue.QueueAsync(new UserMail { RecipientUserKey = UserMailFixture.UserKey, Category = UserMailFixture.News, Subject = "S", Body = "B", TeamKey = teamKey });

        Assert.Null(_mail.Outbox.Single.Mail.TeamKey);
    }

    [Fact]
    public async Task ABatch_KeepsItsTeam_OnEveryMailOfIt()
    {
        await _mail.Queue.QueueBatchAsync(new UserMailBatch { RecipientUserKeys = ["a", "b"], Category = UserMailFixture.News, Subject = "S", Body = "B", TeamKey = " acme " });

        Assert.All(_mail.Outbox.Items, x => Assert.Equal("acme", x.Mail.TeamKey));
        Assert.Equal(2, _mail.Outbox.Items.Count);
    }

    [Fact]
    public async Task TheTeamOfAMail_IsPartOfWhatItsDeliveryReports()
    {
        var queued = await _mail.Queue.QueueAsync(new UserMail { RecipientUserKey = UserMailFixture.UserKey, Category = UserMailFixture.News, Subject = "S", Body = "B", TeamKey = "acme" });

        var delivery = await _mail.Queue.GetAsync(queued.Id, TestContext.Current.CancellationToken);

        Assert.Equal("acme", delivery.TeamKey);
    }

    [Fact]
    public async Task AnInvitation_IsAboutTheTeamItInvitesTo()
    {
        var sender = new UserMailInvitationSender(new UserMailQueue(_mail.Categories, _mail.Outbox, _mail.Wrapped, _mail.Time, _mail.Dispatcher()), new UserMailText(null), _mail.Wrapped);

        await sender.SendInviteAsync(new TeamInviteMail { RecipientEmail = "bea@example.org", RecipientName = "Bea", InviteLink = "https://app.example.com/i/1", TeamKey = "team-1", TeamName = "Alpha" });

        Assert.Equal("team-1", _mail.Outbox.Single.Mail.TeamKey);
    }

    [Fact]
    public async Task AMessage_IsAboutTheOneTeamItsRecipientsAreChosenWithin()
    {
        var fixture = new UserMessageFixture();

        await fixture.Service.SendAsync(UserMessageFixture.Message(new UserMessageRecipients { TeamKeys = [UserMessageFixture.Alpha], EveryMember = true }), TestContext.Current.CancellationToken);

        Assert.All(fixture.Mail.Outbox.Items, x => Assert.Equal(UserMessageFixture.Alpha, x.Mail.TeamKey));
        Assert.NotEmpty(fixture.Mail.Outbox.Items);
    }

    [Fact]
    public async Task AMessageChosenWithinSeveralTeams_OrAllOfThem_IsAboutNone()
    {
        var several = new UserMessageFixture();
        var all = new UserMessageFixture();

        await several.Service.SendAsync(UserMessageFixture.Message(new UserMessageRecipients { TeamKeys = [UserMessageFixture.Alpha, UserMessageFixture.Beta], EveryMember = true }), TestContext.Current.CancellationToken);
        await all.Service.SendAsync(UserMessageFixture.Message(new UserMessageRecipients { AllTeams = true, EveryMember = true }), TestContext.Current.CancellationToken);

        Assert.NotEmpty(several.Mail.Outbox.Items);
        Assert.All(several.Mail.Outbox.Items, x => Assert.Null(x.Mail.TeamKey));
        Assert.NotEmpty(all.Mail.Outbox.Items);
        Assert.All(all.Mail.Outbox.Items, x => Assert.Null(x.Mail.TeamKey));
    }

    [Fact]
    public async Task ANotice_IsAboutItsTeam()
    {
        var fixture = new UserNoticeFixture();

        await fixture.RaiseAsync(UserNoticeFixture.Assigned, "1", recipients: ["anna"]);
        fixture.Mail.Time.Advance(TimeSpan.FromMinutes(2));
        await fixture.RunAsync();

        Assert.Equal(UserNoticeFixture.Alpha, fixture.QueuedTo("anna").Mail.TeamKey);
    }

    [Fact]
    public async Task ASummaryOfNoticesFromSeveralTeams_IsAboutNone()
    {
        var fixture = new UserNoticeFixture();
        fixture.Store.Choices[("anna", UserNoticeFixture.Assigned.Key)] = new UserNoticeSetting { Delivery = UserNoticeDelivery.Digest };

        await fixture.RaiseAsync(UserNoticeFixture.Assigned, "1", UserNoticeFixture.Alpha, ["anna"]);
        await fixture.RaiseAsync(UserNoticeFixture.Assigned, "2", UserNoticeFixture.Beta, ["anna"]);
        fixture.Mail.Time.Advance(UserNoticeFixture.DigestDue - UserMailFixture.Start);
        await fixture.RunAsync();

        var summary = fixture.QueuedTo("anna");
        Assert.Contains("2", summary.Mail.Subject);
        Assert.Null(summary.Mail.TeamKey);
    }

    [Fact]
    public async Task ASummaryOfNoticesFromOneTeam_IsAboutIt()
    {
        var fixture = new UserNoticeFixture();
        fixture.Store.Choices[("anna", UserNoticeFixture.Assigned.Key)] = new UserNoticeSetting { Delivery = UserNoticeDelivery.Digest };

        await fixture.RaiseAsync(UserNoticeFixture.Assigned, "1", UserNoticeFixture.Alpha, ["anna"]);
        await fixture.RaiseAsync(UserNoticeFixture.Assigned, "2", UserNoticeFixture.Alpha, ["anna"]);
        fixture.Mail.Time.Advance(UserNoticeFixture.DigestDue - UserMailFixture.Start);
        await fixture.RunAsync();

        Assert.Equal(UserNoticeFixture.Alpha, fixture.QueuedTo("anna").Mail.TeamKey);
    }
}
