namespace Tharga.Team.Email.Tests;

/// <summary>
/// The verifier is answered only for the one name alignment was decided on, and every other name is refused
/// without asking DNS.
/// </summary>
public class SingleDomainKeyLocatorTests
{
    private const string Aligned = "v=1; a=rsa-sha256; d=example.com; s=s1; h=from:subject; bh=x; b=y";

    private readonly InMemoryKeyLocator _inner = new InMemoryKeyLocator().Publish("example.com", DkimTestMail.Example);

    private SingleDomainKeyLocator Locator(string signature = Aligned) => new(_inner, DkimSignature.Parse(signature));

    [Fact]
    public async Task TheAlignedName_IsAnswered()
    {
        var response = await Locator().QueryTxtAsync("s1._domainkey.example.com");

        Assert.Equal([DkimTestMail.TxtRecord(DkimTestMail.Example)], response.Records);
        Assert.Equal(["s1._domainkey.example.com"], _inner.Asked);
    }

    [Theory]
    [InlineData("s1._domainkey.other.org")]
    [InlineData("s2._domainkey.example.com")]
    [InlineData("s1._domainkey.mail.example.com")]
    [InlineData("s1._domainkey.x._domainkey.example.com")]
    public async Task AnyOtherName_IsRefusedWithoutAsking(string name)
    {
        var response = await Locator().QueryTxtAsync(name);

        Assert.Empty(response.Records);
        Assert.Empty(_inner.Asked);
    }

    [Fact]
    public async Task ASignatureWithoutASelector_IsAnsweredForNothing()
    {
        var response = await Locator("v=1; d=example.com; h=from; bh=x; b=y").QueryTxtAsync("s1._domainkey.example.com");

        Assert.Empty(response.Records);
        Assert.Empty(_inner.Asked);
    }
}
