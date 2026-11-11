using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The per-case reply address an answer carries: a short tracking token, assigned the first time it is needed, with the
/// full case id still working in an address sent before tokens existed (Tharga/Team#465).
/// </summary>
public partial class CaseReplyAddressTests
{
    private const string TeamKey = "acme";
    private const string OpeningId = "opening-1@example.net";

    [GeneratedRegex(@"^support\+tracking-[0-9a-f]{12}@example\.net$")]
    private static partial Regex ShortAddress();

    [Fact]
    public async Task ANewAnswer_CarriesAShortTrackingAddress_NotTheCaseId()
    {
        var (channel, mail, store) = await Build();

        await channel.PostAsync(Binding(), Message());

        var replyTo = SentReplyTo(mail);
        Assert.Matches(ShortAddress(), replyTo);
        Assert.DoesNotContain("case-1", replyTo);
        Assert.Equal(TokenIn(replyTo), (await store.GetCaseAsync(TeamKey, "case-1")).TrackingToken);
    }

    [Fact]
    public async Task ACaseWithNoToken_IsGivenOneOnItsNextAnswer_AndKeepsIt()
    {
        var (channel, mail, store) = await Build();
        Assert.Null((await store.GetCaseAsync(TeamKey, "case-1")).TrackingToken);

        await channel.PostAsync(Binding(), Message());
        var first = SentReplyTo(mail);
        mail.ClearReceivedCalls();
        await channel.PostAsync(Binding(), Message());

        Assert.Equal(first, SentReplyTo(mail));
    }

    [Fact]
    public async Task AReplyToTheShortAddress_ResolvesToItsCase_AndSoDoesTheFullCaseIdOfAnOlderAddress()
    {
        var (channel, mail, store) = await Build();
        await channel.PostAsync(Binding(), Message());
        var token = TokenIn(SentReplyTo(mail));

        Assert.Equal("case-1", (await CaseReplyAddress.FindAsync(store, token, default))?.Id);
        Assert.Equal("case-1", (await CaseReplyAddress.FindAsync(store, "case-1", default))?.Id);
        Assert.Null(await CaseReplyAddress.FindAsync(store, "tracking-000000000000", default));
        Assert.Null(await CaseReplyAddress.FindAsync(store, " ", default));
    }

    [Fact]
    public async Task APreviewAnswer_ShowsTheAddressTheRealOneUses()
    {
        var (channel, mail, _) = await Build();

        var preview = await ((ISupportAnswerMail)channel).PreviewAsync(Case(), Binding(), Message());
        await channel.PostAsync(Binding(), Message());

        Assert.Equal(preview.ReplyTo, SentReplyTo(mail));
    }

    [Fact]
    public async Task ATokenAlreadyTaken_IsNeverReused_AnotherIsTried()
    {
        var store = Substitute.For<ISupportCaseStore>();
        var tried = new List<string>();
        store.TrySetTrackingTokenAsync(TeamKey, "case-1", Arg.Do<string>(tried.Add), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string>(null), Task.FromResult<string>(null), Task.FromResult("tracking-aaaaaaaaaaaa"));

        var replyTo = await CaseReplyAddress.ForAsync(store, Options(), Case(), null, default);

        Assert.Equal("support+tracking-aaaaaaaaaaaa@example.net", replyTo);
        Assert.Equal(3, tried.Count);
        Assert.Equal(3, tried.Distinct().Count());
    }

    [Fact]
    public async Task WhenNoTokenCanBeHeld_TheAddressCarriesTheCaseId_SoTheAnswerStillGoes()
    {
        // A store written before tokens existed answers null every time; a fake one answers an empty string.
        var none = Substitute.For<ISupportCaseStore>();
        none.TrySetTrackingTokenAsync(TeamKey, "case-1", Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Task.FromResult<string>(null));
        var empty = Substitute.For<ISupportCaseStore>();

        Assert.Equal("support+case-1@example.net", await CaseReplyAddress.ForAsync(none, Options(), Case(), null, default));
        Assert.Equal("support+case-1@example.net", await CaseReplyAddress.ForAsync(empty, Options(), Case(), null, default));
    }

    [Fact]
    public async Task TheTrackingPrefix_CanBeChanged()
    {
        var store = Substitute.For<ISupportCaseStore>();
        string drawn = null;
        store.TrySetTrackingTokenAsync(TeamKey, "case-1", Arg.Do<string>(x => drawn = x), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(x.ArgAt<string>(2)));

        var replyTo = await CaseReplyAddress.ForAsync(store, Options(prefix: "ticket-"), Case(), null, default);

        Assert.Matches(@"^support\+ticket-[0-9a-f]{12}@example\.net$", replyTo);
        Assert.StartsWith("ticket-", drawn);
    }

    [Fact]
    public async Task AMailboxWithoutPerCaseReplyTo_GetsNoAddress_AndNoTokenIsWritten()
    {
        var store = Substitute.For<ISupportCaseStore>();

        Assert.Null(await CaseReplyAddress.ForAsync(store, Options(perCase: false), Case(), null, default));
        await store.DidNotReceiveWithAnyArgs().TrySetTrackingTokenAsync(default, default, default, default);
    }

    [Fact]
    public void ATokenIsTwelveHexCharactersAfterThePrefix_AndDiffersEveryTime()
    {
        var tokens = Enumerable.Range(0, 50).Select(_ => CaseReplyAddress.NewToken("tracking-")).ToArray();

        Assert.All(tokens, x => Assert.Matches("^tracking-[0-9a-f]{12}$", x));
        Assert.Equal(50, tokens.Distinct().Count());
    }

    private static MailOptions Options(bool perCase = true, string prefix = "tracking-")
        => new() { FromAddress = "support@example.net", PerCaseReplyTo = perCase, TrackingPrefix = prefix };

    private static string SentReplyTo(IMailClient mail)
        => mail.ReceivedCalls().Where(x => x.GetMethodInfo().Name == nameof(IMailClient.SendAsync)).Select(x => ((OutboundMail)x.GetArguments()[0]).ReplyTo).Last();

    private static string TokenIn(string address) => address[(address.IndexOf('+') + 1)..address.IndexOf('@')];

    private static SupportCase Case() => new()
    {
        Id = "case-1",
        TeamKey = TeamKey,
        AuthorIdentity = "sub-1",
        AuthorName = "A User",
        Subject = "Export is empty",
        Status = SupportCaseStatus.Open,
        CreatedAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
        MessageCount = 1,
        Bindings = [Binding()]
    };

    private static SupportChannelBinding Binding() => new()
    {
        ChannelType = SupportChannelType.Email,
        ExternalId = OpeningId,
        Address = "user@example.com"
    };

    private static SupportMessage Message() => new()
    {
        Sequence = 2,
        Kind = SupportMessageKind.User,
        AuthorIdentity = "sub-2",
        AuthorName = "Support Agent",
        Body = "Looking into it.",
        SentAt = new DateTime(2026, 9, 1, 9, 0, 0, DateTimeKind.Utc)
    };

    private static async Task<(ISupportChannel Channel, IMailClient Mail, InMemorySupportCaseStore Store)> Build()
    {
        var mail = Substitute.For<IMailClient>();
        mail.CanSend.Returns(true);
        mail.SendAsync(Arg.Any<OutboundMail>(), Arg.Any<CancellationToken>()).Returns(MailSendResult.Ok("reply-1@example.net"));

        var store = new InMemorySupportCaseStore();
        await store.AddCaseAsync(Case(), new SupportMessage
        {
            Sequence = 1,
            Kind = SupportMessageKind.User,
            AuthorIdentity = "sub-1",
            AuthorName = "A User",
            Body = "Nothing comes out.",
            SentAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc)
        });

        return (new EmailSupportChannel(mail, store, Microsoft.Extensions.Options.Options.Create(Options())), mail, store);
    }
}
