using MimeKit;

namespace Tharga.Team.Email.Tests;

/// <summary>
/// Whether a received mail demonstrably comes from its sender's domain — with keys made for the test, so every
/// case is a real signature verified the way MimeKit verifies one.
/// </summary>
/// <remarks>
/// <b>The refusals are the point.</b> A verified sender can open a case inside a customer's team, so each way a
/// stranger could pass for somebody else is asserted to fail: a signature from their own domain, a signature
/// claiming the victim's domain made with the wrong key, a body changed after signing, and a second
/// <c>From</c> placed above a genuinely signed one.
/// </remarks>
public class DkimSenderVerifierTests
{
    private readonly InMemoryKeyLocator _keys = new InMemoryKeyLocator()
        .Publish("example.com", DkimTestMail.Example)
        .Publish("other.org", DkimTestMail.Other)
        .Publish("mail.example.com", DkimTestMail.Other);

    private DkimSenderVerifier Verifier => new(_keys);

    [Fact]
    public async Task AMailSignedByTheSendersOwnDomain_IsVerified()
    {
        Assert.Equal("example.com", await Verifier.VerifiedDomainAsync(DkimTestMail.Signed("example.com")));
    }

    [Fact]
    public async Task ASenderOnASubdomainOfTheSigner_IsVerified()
    {
        var message = DkimTestMail.Signed("example.com", from: "anna@mail.example.com");

        Assert.Equal("example.com", await Verifier.VerifiedDomainAsync(message));
    }

    [Fact]
    public async Task ASignerOnASubdomainOfTheSender_DoesNotVouchForTheParent()
    {
        var message = DkimTestMail.Signed("mail.example.com", DkimTestMail.Other);

        Assert.Null(await Verifier.VerifiedDomainAsync(message));
    }

    /// <summary>
    /// <b>A valid signature from another domain only says that domain relayed it.</b> And it is never looked up:
    /// alignment is decided before any key is fetched, so a stranger's domain costs no DNS.
    /// </summary>
    [Fact]
    public async Task AValidSignatureFromAnotherDomain_DoesNotVerifyTheSender()
    {
        var message = DkimTestMail.Signed("other.org", DkimTestMail.Other);

        Assert.Null(await Verifier.VerifiedDomainAsync(message));
        Assert.Empty(_keys.Asked);
    }

    [Fact]
    public async Task ASignatureClaimingTheSendersDomainMadeWithAnotherKey_IsNotVerified()
    {
        var forged = DkimTestMail.Signed("example.com", DkimTestMail.Other);

        Assert.Null(await Verifier.VerifiedDomainAsync(forged));
    }

    [Fact]
    public async Task ABodyChangedAfterSigning_IsNotVerified()
    {
        var raw = DkimTestMail.SignedRaw("example.com").Replace(DkimTestMail.Body, "Please reset the owner's password.");

        Assert.Null(await Verifier.VerifiedDomainAsync(DkimTestMail.Received(raw)));
    }

    /// <summary>
    /// <b>The replay that a header count stops.</b> A signature covers the last <c>From</c>, the sender is read
    /// from the first, so a second one added above a genuinely signed mail would pass the mail off as somebody
    /// else's on the same domain.
    /// </summary>
    [Fact]
    public async Task ASecondFromAddedAboveASignedOne_IsNotVerified()
    {
        var raw = "From: ceo@example.com\r\n" + DkimTestMail.SignedRaw("example.com");

        Assert.Null(await Verifier.VerifiedDomainAsync(DkimTestMail.Received(raw)));
    }

    [Fact]
    public async Task AnUnsignedMail_IsNotVerified()
    {
        Assert.Null(await Verifier.VerifiedDomainAsync(DkimTestMail.Unsigned()));
    }

    [Fact]
    public async Task AKeyThatCannotBeFound_MeansNotVerified_RatherThanAnError()
    {
        var verifier = new DkimSenderVerifier(new InMemoryKeyLocator());

        Assert.Null(await verifier.VerifiedDomainAsync(DkimTestMail.Signed("example.com")));
    }

    /// <summary>The mail decides how many signatures it carries, and each can cost a DNS lookup.</summary>
    [Fact]
    public async Task NoMoreThanAFewSignaturesAreChecked()
    {
        var raw = DkimTestMail.SignedRaw("example.com", DkimTestMail.Other);
        var signature = FirstHeader(raw);
        Assert.StartsWith("DKIM-Signature:", signature);

        var flooded = string.Concat(Enumerable.Repeat(signature, 10)) + raw;
        var keys = new InMemoryKeyLocator();

        Assert.Null(await new DkimSenderVerifier(keys).VerifiedDomainAsync(DkimTestMail.Received(flooded)));
        Assert.Equal(DkimSenderVerifier.MaxVerifications, keys.Asked.Count);
    }

    /// <summary>The first header of <paramref name="raw"/> with its folded continuation lines.</summary>
    private static string FirstHeader(string raw)
    {
        var lines = raw.Split("\r\n");
        var length = 1 + lines.Skip(1).TakeWhile(x => x.StartsWith(' ') || x.StartsWith('\t')).Count();

        return string.Join("\r\n", lines.Take(length)) + "\r\n";
    }

    /// <summary>The wiring: what the verifier decides is what the read mail says.</summary>
    [Fact]
    public async Task ReadingAVerifiedMail_SaysSoAndNamesTheDomain()
    {
        var mail = await InboundMailReader.ReadAsync(DkimTestMail.Signed("example.com"), Verifier);

        Assert.True(mail.SenderVerified);
        Assert.Equal("example.com", mail.SenderDomain);
        Assert.Equal(DkimTestMail.Sender, mail.From);
    }

    [Fact]
    public async Task ReadingAForgedMail_LeavesItUnverified()
    {
        var mail = await InboundMailReader.ReadAsync(DkimTestMail.Signed("example.com", DkimTestMail.Other), Verifier);

        Assert.False(mail.SenderVerified);
        Assert.Null(mail.SenderDomain);
    }

    [Fact]
    public async Task ReadingWithNoVerifier_LeavesItUnverified()
    {
        var mail = await InboundMailReader.ReadAsync(DkimTestMail.Signed("example.com"), null);

        Assert.False(mail.SenderVerified);
    }

    [Fact]
    public void TheSynchronousRead_NeverClaimsVerification()
    {
        Assert.False(InboundMailReader.Read(DkimTestMail.Signed("example.com")).SenderVerified);
    }
}
