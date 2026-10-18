using Tharga.Team.Email.Users;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>Who a sent message went to: each recipient in the order they were queued, with the address the mail went to, where it stands, why and since when, filtered by status and read a page at a time.</summary>
public class UserMessageRecipientTests
{
    private static readonly UserMessageRecipients Alpha = new() { TeamKeys = [UserMessageFixture.Alpha], EveryMember = true };

    private static async Task<(UserMessageFixture Fixture, SentUserMessage Sent)> SentAndDispatchedAsync()
    {
        var fixture = new UserMessageFixture();
        var sent = await fixture.Service.SendAsync(UserMessageFixture.Message(Alpha), TestContext.Current.CancellationToken);
        await fixture.Mail.Store.SetChoiceAsync("bo", UserMailFixture.News, false, TestContext.Current.CancellationToken);
        fixture.Mail.Recipients.Add(new UserMailRecipient { Key = "cia", Name = "Cia" });
        fixture.Mail.Time.Advance(TimeSpan.FromMinutes(3));

        await fixture.Mail.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken);
        fixture.Mail.Audit.Entries.Clear();

        return (fixture, sent);
    }

    [Fact]
    public async Task EachRecipient_IsListedInQueueOrder_WithTheAddressItWentTo_WhereItStands_AndWhy()
    {
        var (fixture, sent) = await SentAndDispatchedAsync();

        var page = await fixture.Service.GetRecipientsAsync(sent.Id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["anna", "bo", "cia"], page.Items.Select(x => x.UserKey));
        Assert.Null(page.NextCursor);

        var anna = page.Items[0];
        Assert.Equal("Anna", anna.Name);
        Assert.Equal("anna@example.org", anna.Address);
        Assert.Equal(UserMailStatus.Sent, anna.Status);
        Assert.NotNull(anna.MessageId);
        Assert.Equal(UserMailFixture.Start.AddMinutes(3), anna.ChangedAt);

        Assert.Equal(UserMailStatus.Skipped, page.Items[1].Status);
        Assert.Equal(UserMailDispatcher.OptedOutReason, page.Items[1].Reason);
        Assert.Null(page.Items[1].Address);

        Assert.Equal(UserMailStatus.Failed, page.Items[2].Status);
        Assert.Equal("The user record holds no address.", page.Items[2].Reason);
        Assert.Equal("Cia", page.Items[2].Name);
    }

    [Fact]
    public async Task ARecipientWaitingToBeSent_StandsQueued_SinceItWasQueued_WithNoAddressYet()
    {
        var fixture = new UserMessageFixture();
        var sent = await fixture.Service.SendAsync(UserMessageFixture.Message(Alpha), TestContext.Current.CancellationToken);

        var page = await fixture.Service.GetRecipientsAsync(sent.Id, cancellationToken: TestContext.Current.CancellationToken);

        Assert.All(page.Items, x =>
        {
            Assert.Equal(UserMailStatus.Queued, x.Status);
            Assert.Equal(UserMailFixture.Start, x.ChangedAt);
            Assert.Null(x.Address);
            Assert.Null(x.MessageId);
        });
    }

    [Fact]
    public async Task TheList_IsFilteredByStatus()
    {
        var (fixture, sent) = await SentAndDispatchedAsync();

        var failed = await fixture.Service.GetRecipientsAsync(sent.Id, UserMailStatus.Failed, cancellationToken: TestContext.Current.CancellationToken);
        var sentOnly = await fixture.Service.GetRecipientsAsync(sent.Id, UserMailStatus.Sent, cancellationToken: TestContext.Current.CancellationToken);
        var waiting = await fixture.Service.GetRecipientsAsync(sent.Id, UserMailStatus.Queued, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["cia"], failed.Items.Select(x => x.UserKey));
        Assert.Equal(["anna"], sentOnly.Items.Select(x => x.UserKey));
        Assert.Empty(waiting.Items);
    }

    [Fact]
    public async Task TheList_IsReadAPageAtATime_ByTheCursorEachPageReturns()
    {
        var (fixture, sent) = await SentAndDispatchedAsync();

        var first = await fixture.Service.GetRecipientsAsync(sent.Id, pageSize: 2, cancellationToken: TestContext.Current.CancellationToken);
        var second = await fixture.Service.GetRecipientsAsync(sent.Id, cursor: first.NextCursor, pageSize: 2, cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal(["anna", "bo"], first.Items.Select(x => x.UserKey));
        Assert.NotNull(first.NextCursor);
        Assert.Equal(["cia"], second.Items.Select(x => x.UserKey));
        Assert.Null(second.NextCursor);
    }

    [Fact]
    public async Task TheAddress_IsRecordedAsTheUserRecordHadItWhenSent_InLowerCase()
    {
        var fixture = new UserMessageFixture();
        var sent = await fixture.Service.SendAsync(UserMessageFixture.Message(Alpha), TestContext.Current.CancellationToken);
        fixture.Mail.Recipients.Add(new UserMailRecipient { Key = "anna", Name = "Anna", EMail = " Anna.Andersson@Example.ORG " });

        await fixture.Mail.Dispatcher().RunAsync("me", TestContext.Current.CancellationToken);
        fixture.Mail.Recipients.Add(new UserMailRecipient { Key = "anna", Name = "Anna", EMail = "anna.new@example.org" });

        var anna = (await fixture.Service.GetRecipientsAsync(sent.Id, UserMailStatus.Sent, cancellationToken: TestContext.Current.CancellationToken)).Items.Single(x => x.UserKey == "anna");
        Assert.Equal("anna.andersson@example.org", anna.Address);
    }

    [Fact]
    public async Task ARecipientWhoNoLongerExists_IsListedWithoutAName()
    {
        var (fixture, sent) = await SentAndDispatchedAsync();
        fixture.Mail.Recipients.Remove("anna");

        var anna = (await fixture.Service.GetRecipientsAsync(sent.Id, cancellationToken: TestContext.Current.CancellationToken)).Items.Single(x => x.UserKey == "anna");

        Assert.Null(anna.Name);
        Assert.Equal("anna@example.org", anna.Address);
    }

    [Fact]
    public async Task AnUnknownMessage_HasNoRecipients()
    {
        Assert.Null(await new UserMessageFixture().Service.GetRecipientsAsync("nothing", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ACursorTheListDidNotIssue_IsRefused()
    {
        var (fixture, sent) = await SentAndDispatchedAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.GetRecipientsAsync(sent.Id, cursor: "not-a-cursor", cancellationToken: TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ReadingTheRecipients_IsAudited_WithTheMessageTheFilterAndTheCount_AndNeverAnAddress()
    {
        var (fixture, sent) = await SentAndDispatchedAsync();

        await fixture.Service.GetRecipientsAsync(sent.Id, UserMailStatus.Sent, cancellationToken: TestContext.Current.CancellationToken);

        var entry = Assert.Single(fixture.Mail.Audit.Entries);
        Assert.Equal(AuditEventType.ServiceCall, entry.EventType);
        Assert.Equal(UserMessageAudit.Feature, entry.Feature);
        Assert.Equal(UserMessageAudit.RecipientsAction, entry.Action);
        Assert.Equal(sent.Id, entry.Metadata[UserMessageAudit.MessageId]);
        Assert.Equal(nameof(UserMailStatus.Sent), entry.Metadata[UserMessageAudit.Status]);
        Assert.Equal("1", entry.Metadata[UserMessageAudit.Count]);
        Assert.DoesNotContain(entry.Metadata.Values, x => x.Contains('@'));
    }
}
