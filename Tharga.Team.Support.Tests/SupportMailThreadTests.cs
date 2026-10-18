using NSubstitute;
using Tharga.Team.Service.Email;
using Tharga.Team.Support.Cases;

namespace Tharga.Team.Support.Tests;

public class SupportMailThreadTests
{
    private static SupportChannelBinding Mail(string id, string address = "anna@example.org") =>
        new() { ChannelType = SupportChannelType.Email, ExternalId = id, Address = address };

    private static SupportCase Case(params SupportChannelBinding[] bindings) => new()
    {
        Id = "case-1",
        AuthorIdentity = null,
        AuthorName = "anna@example.org",
        Subject = "Hej",
        Status = SupportCaseStatus.Open,
        CreatedAt = DateTime.UtcNow,
        MessageCount = 1,
        Bindings = bindings
    };

    private static SupportMessage Message(int sequence, string author, string body, SupportMessageKind kind = SupportMessageKind.User) =>
        new() { Sequence = sequence, Kind = kind, AuthorName = author, Body = body, SentAt = new DateTime(2026, 10, 17, 12, sequence, 0, DateTimeKind.Utc) };

    [Fact]
    public void AnAnswerNamesTheWholeThread_AndRepliesToItsNewestMail()
    {
        var thread = EmailSupportChannel.ThreadOf(Case(Mail("<first>"), Mail("<answer>"), Mail("<reply>")), Mail("<first>"));

        Assert.Equal(["<first>", "<answer>", "<reply>"], thread);
        Assert.Equal("<reply>", thread[^1]);
    }

    [Fact]
    public void ACaseWithNoRecordedThread_StillAnswersTheMailItCameFrom()
    {
        Assert.Equal(["<first>"], EmailSupportChannel.ThreadOf(Case(), Mail("<first>")));
    }

    [Fact]
    public void AnAnswerQuotesTheConversationSoFar_NewestFirst()
    {
        var body = EmailSupportChannel.Body(Message(3, "Sam", "Here is the answer."), [Message(2, "anna@example.org", "Any news?"), Message(1, "anna@example.org", "It does not work.\nAt all.")]);

        Assert.StartsWith("Sam:\n\nHere is the answer.", body);
        Assert.True(body.IndexOf("> Any news?", StringComparison.Ordinal) < body.IndexOf("> It does not work.", StringComparison.Ordinal));
        Assert.Contains("> At all.", body);
        Assert.Contains("2026-10-17 12:01 UTC, anna@example.org:", body);
    }

    [Fact]
    public void AnAnswerWithNothingBefore_IsJustTheAnswer()
    {
        Assert.Equal("Sam:\n\nHej", EmailSupportChannel.Body(Message(2, "Sam", "Hej"), []));
    }

    [Fact]
    public void AReplyFromTheCorrespondent_Corresponds()
    {
        Assert.True(EmailEventHandler.Corresponds(Mail("<first>"), "Anna@Example.org", null));
    }

    [Fact]
    public void AReplyFromSomebodyElse_DoesNot()
    {
        Assert.False(EmailEventHandler.Corresponds(Mail("<first>"), "mallory@example.org", null));
    }

    [Fact]
    public void OutsideProduction_AReplyFromTheAddressTheAnswerWasRedirectedTo_Corresponds()
    {
        var policy = Substitute.For<IOutboundMailPolicy>();
        policy.Decide("anna@example.org", Arg.Any<string>()).Returns(new OutboundMailDecision(OutboundMailAction.Redirect, "dev@example.com", ""));

        Assert.True(EmailEventHandler.Corresponds(Mail("<first>"), "dev@example.com", policy));
        Assert.False(EmailEventHandler.Corresponds(Mail("<first>"), "mallory@example.org", policy));
    }

    [Fact]
    public void InProduction_TheRedirectRuleOpensNothing()
    {
        var policy = Substitute.For<IOutboundMailPolicy>();
        policy.Decide("anna@example.org", Arg.Any<string>()).Returns(new OutboundMailDecision(OutboundMailAction.Deliver, "anna@example.org", ""));

        Assert.False(EmailEventHandler.Corresponds(Mail("<first>"), "dev@example.com", policy));
    }
}
