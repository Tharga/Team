using MimeKit;
using Tharga.Team.Email.Users;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>
/// A person answering by mail: the name this one mail is sent under, and the headers that thread it with the mail it answers. The address never changes, and a value that
/// comes from outside can never write a header of its own.
/// </summary>
public class UserMailSenderNameAndThreadingTests
{
    private const string Claimant = "test";

    private readonly UserMailFixture _fixture = new();

    private UserMail Mail(Func<UserMail, UserMail> change = null)
    {
        var mail = new UserMail { RecipientUserKey = UserMailFixture.UserKey, Category = UserMailFixture.News, Subject = "Your case", Body = "Hello." };

        return change == null ? mail : change(mail);
    }

    /// <summary>Queues the mail, sends it, and builds the message as the SMTP client would send it.</summary>
    private async Task<(OutboundMail Sent, MimeMessage Message)> SendAsync(UserMail mail)
    {
        await _fixture.Queue.QueueAsync(mail);
        await _fixture.Dispatcher().SendNextAsync(Claimant, TestContext.Current.CancellationToken);

        var sent = Assert.Single(_fixture.Mail.Sent);

        return (sent, MailMessageFactory.Create(sent, _fixture.Options.Mail));
    }

    // ---- the sender's name ----

    [Fact]
    public async Task AMailWithAFromName_IsSentUnderThatName_FromTheConfiguredAddress()
    {
        var (_, message) = await SendAsync(Mail(m => m with { FromName = "Anna Lind" }));

        var from = Assert.Single(message.From.Mailboxes);
        Assert.Equal("Anna Lind", from.Name);
        Assert.Equal("hello@example.com", from.Address);
    }

    [Fact]
    public async Task AMailWithNoFromName_IsSentAsBefore_UnderTheConfiguredSendersName()
    {
        var (sent, message) = await SendAsync(Mail());

        Assert.Null(sent.From);
        Assert.Equal("Example", Assert.Single(message.From.Mailboxes).Name);
    }

    [Fact]
    public async Task TheFromName_IsKeptWithTheMail_SoTheLogRecordsIt()
    {
        var queued = await _fixture.Queue.QueueAsync(Mail(m => m with { FromName = "Anna Lind" }));

        Assert.Equal("Anna Lind", _fixture.Outbox.Single.Mail.FromName);
        Assert.Equal("Anna Lind", (await _fixture.Queue.GetAsync(queued.Id, TestContext.Current.CancellationToken)).FromName);

        await _fixture.Dispatcher().SendNextAsync(Claimant, TestContext.Current.CancellationToken);

        Assert.Equal("Anna Lind", (await _fixture.Queue.GetAsync(queued.Id, TestContext.Current.CancellationToken)).FromName);
    }

    /// <summary>Only the name is the mail's, so the address recorded as the sender is still the configured one.</summary>
    [Fact]
    public async Task TheAddressTheMailIsRecordedAsSentFrom_IsStillTheConfiguredOne()
    {
        await SendAsync(Mail(m => m with { FromName = "Anna Lind" }));

        Assert.Equal("hello@example.com", _fixture.Outbox.Single.FromAddress);
    }

    private UserMailQueue QueueThatSendsAtOnce() => new(_fixture.Categories, _fixture.Outbox, _fixture.Wrapped, _fixture.Time, _fixture.Dispatcher(), notifier: _fixture.Notifier);

    [Fact]
    public async Task AMailAtOnce_IsSentUnderTheNameToo()
    {
        var delivery = await QueueThatSendsAtOnce().SendNowAsync(Mail(m => m with { FromName = "Anna Lind" }), TestContext.Current.CancellationToken);

        Assert.Equal(UserMailStatus.Sent, delivery.Status);
        Assert.Equal("Anna Lind", delivery.FromName);
        Assert.Equal("Anna Lind", MailboxAddress.Parse(Assert.Single(_fixture.Mail.Sent).From).Name);
    }

    [Theory]
    [InlineData("Anna\r\nBcc: someone@example.org", "Anna Bcc: someone@example.org")]
    [InlineData("Anna\nLind", "Anna Lind")]
    [InlineData("Anna\rLind", "Anna Lind")]
    [InlineData("  Anna   Lind  ", "Anna Lind")]
    [InlineData("Anna <evil@example.org>", "Anna evil@example.org")]
    [InlineData("Anna\u0007Lind", "AnnaLind")]
    public void AFromName_IsCleaned_OfLineBreaks_ControlCharactersAndAngleBrackets(string given, string expected)
    {
        Assert.Equal(expected, UserMailQueue.FromName(given));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("<>")]
    public void ANameWithNothingLeft_IsNoName(string given)
    {
        Assert.Null(UserMailQueue.FromName(given));
    }

    [Fact]
    public void ANameLongerThanAPersonHas_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => UserMailQueue.FromName(new string('a', UserMailQueue.MaxFromNameLength + 1)));
        Assert.Equal(UserMailQueue.MaxFromNameLength, UserMailQueue.FromName(new string('a', UserMailQueue.MaxFromNameLength)).Length);
    }

    /// <summary>A line break in the name is how a header would be injected: it must reach the wire as part of the name and never as a header.</summary>
    [Fact]
    public async Task ALineBreakInTheName_NeverBecomesAHeader()
    {
        var (_, message) = await SendAsync(Mail(m => m with { FromName = "Anna\r\nBcc: someone@example.org\r\nX-Injected: yes" }));

        Assert.Empty(message.Bcc);
        Assert.False(message.Headers.Contains("X-Injected"));
        Assert.DoesNotContain("\n", Assert.Single(message.From.Mailboxes).Name);
    }

    [Fact]
    public async Task ANameWithAccentsAndPunctuation_SurvivesIntoTheFromLine()
    {
        var (_, message) = await SendAsync(Mail(m => m with { FromName = "Åsa Lind, Östersund (kommunen)" }));

        Assert.Equal("Åsa Lind, Östersund (kommunen)", Assert.Single(message.From.Mailboxes).Name);
        Assert.Equal("hello@example.com", Assert.Single(message.From.Mailboxes).Address);
    }

    // ---- threading ----

    [Fact]
    public async Task AMailWithInReplyToAndReferences_CarriesBothHeaders()
    {
        var (_, message) = await SendAsync(Mail(m => m with
        {
            InReplyTo = "<c@example.org>",
            References = ["<a@example.org>", "<b@example.org>", "<c@example.org>"]
        }));

        Assert.Equal("c@example.org", message.InReplyTo);
        Assert.Equal(["a@example.org", "b@example.org", "c@example.org"], message.References.ToArray());
        Assert.True(message.Headers.Contains(HeaderId.InReplyTo));
        Assert.True(message.Headers.Contains(HeaderId.References));
    }

    /// <summary>A mail that answers one and does not list the chain still threads: the answered mail is the last reference.</summary>
    [Fact]
    public async Task InReplyToAlone_IsAlsoWrittenAsTheLastReference()
    {
        var (_, message) = await SendAsync(Mail(m => m with { InReplyTo = "c@example.org" }));

        Assert.Equal("c@example.org", message.InReplyTo);
        Assert.Equal(["c@example.org"], message.References.ToArray());
    }

    [Fact]
    public async Task AMailWithoutThem_CarriesNeitherHeader()
    {
        var (sent, message) = await SendAsync(Mail());

        Assert.Null(sent.InReplyTo);
        Assert.Null(sent.References);
        Assert.False(message.Headers.Contains(HeaderId.InReplyTo));
        Assert.False(message.Headers.Contains(HeaderId.References));
    }

    [Fact]
    public async Task EmptyReferences_WriteNothing()
    {
        var (_, message) = await SendAsync(Mail(m => m with { References = [], InReplyTo = "  " }));

        Assert.False(message.Headers.Contains(HeaderId.InReplyTo));
        Assert.False(message.Headers.Contains(HeaderId.References));
    }

    [Fact]
    public async Task TheMessageIdOfASentMail_IsReturnedSoItCanBeTheNextMailsReference()
    {
        var first = await QueueThatSendsAtOnce().SendNowAsync(Mail(), TestContext.Current.CancellationToken);
        Assert.Equal("<m1@example.com>", first.MessageId);

        await _fixture.Queue.QueueAsync(Mail(m => m with { InReplyTo = first.MessageId, References = [first.MessageId] }), TestContext.Current.CancellationToken);
        await _fixture.Dispatcher().SendNextAsync(Claimant, TestContext.Current.CancellationToken);
        var message = MailMessageFactory.Create(_fixture.Mail.Sent[^1], _fixture.Options.Mail);

        Assert.Equal("m1@example.com", message.InReplyTo);
        Assert.Equal(["m1@example.com"], message.References.ToArray());
    }

    [Theory]
    [InlineData("<abc@example.org>", "abc@example.org")]
    [InlineData("abc@example.org", "abc@example.org")]
    [InlineData("  <abc@example.org>  ", "abc@example.org")]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("<abc@example.org>\r\n", "abc@example.org")]
    public void AMessageId_IsKeptWithoutItsBrackets(string given, string expected)
    {
        Assert.Equal(expected, UserMailQueue.MessageId(given, "InReplyTo"));
    }

    [Theory]
    [InlineData("abc@example.org\r\nBcc: someone@example.org")]
    [InlineData("abc@example.org\nBcc: x@example.org")]
    [InlineData("abc@example.org\u0000")]
    [InlineData("abc\r@example.org")]
    [InlineData("<abc@example.org")]
    [InlineData("abc@example.org>")]
    [InlineData("<<abc@example.org>>")]
    [InlineData("<>")]
    [InlineData("<abc@exam<ple.org>")]
    [InlineData("abc @example.org")]
    [InlineData("two@example.org three@example.org")]
    public void AValueThatIsNotOneMessageId_IsRefused(string given)
    {
        Assert.Throws<ArgumentException>(() => UserMailQueue.MessageId(given, "InReplyTo"));
    }

    [Fact]
    public void AMessageIdLongerThanOneCanBe_IsRefused()
    {
        Assert.Throws<ArgumentException>(() => UserMailQueue.MessageId($"{new string('a', UserMailQueue.MaxMessageIdLength)}@example.org", "InReplyTo"));
    }

    [Theory]
    [InlineData("abc@example.org\r\nBcc: someone@example.org")]
    [InlineData("<abc@example.org")]
    public async Task AnInReplyToThatCouldInjectAHeader_IsRefusedWhenQueued_AndNothingIsSent(string value)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Queue.QueueAsync(Mail(m => m with { InReplyTo = value })));

        Assert.Empty(_fixture.Mail.Sent);
    }

    [Theory]
    [InlineData("abc@example.org\r\nBcc: someone@example.org")]
    [InlineData("abc@example.org\nX: y")]
    [InlineData("abc@example.org>")]
    [InlineData("<abc@example.org")]
    public async Task AReferenceThatCouldInjectAHeader_IsRefusedWhenQueued(string value)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Queue.QueueAsync(Mail(m => m with { References = ["ok@example.org", value] })));
    }

    [Fact]
    public async Task ARefusedHeaderValue_IsRecordedInTheMailLog_LikeAnyOtherRefusal()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Queue.QueueAsync(Mail(m => m with { InReplyTo = "<abc@example.org" })));

        var refused = Assert.Single(_fixture.Outbox.Items);
        Assert.True(refused.Refused);
        Assert.Equal(UserMailStatus.Failed, refused.Status);
    }

    [Fact]
    public async Task References_AreStoredOnceEach_InTheOrderGiven()
    {
        await _fixture.Queue.QueueAsync(Mail(m => m with { References = ["<a@example.org>", "b@example.org", "a@example.org", " <c@example.org> "] }));

        Assert.Equal(["a@example.org", "b@example.org", "c@example.org"], _fixture.Outbox.Single.Mail.References);
    }

    [Fact]
    public async Task TooManyReferences_AreRefused()
    {
        var many = Enumerable.Range(0, UserMailQueue.MaxReferences + 1).Select(i => $"m{i}@example.org").ToArray();

        await Assert.ThrowsAsync<ArgumentException>(() => _fixture.Queue.QueueAsync(Mail(m => m with { References = many })));
        await _fixture.Queue.QueueAsync(Mail(m => m with { References = [.. many.Take(UserMailQueue.MaxReferences)] }));
    }

    [Fact]
    public async Task NoReferencesAtAll_AreStoredAsNone()
    {
        await _fixture.Queue.QueueAsync(Mail(m => m with { References = [] }));

        Assert.Null(_fixture.Outbox.Single.Mail.References);
    }
}
