namespace Tharga.Team.Email.Tests;

/// <summary>
/// Reading the tags of a <c>DKIM-Signature</c> header, and which shapes may vouch for a sender at all.
/// </summary>
public class DkimSignatureTests
{
    private const string Gmail =
        "v=1; a=rsa-sha256; c=relaxed/relaxed;\r\n        d=Gmail.com; s=20230601; t=1760000000;\r\n" +
        "        h=to:subject:message-id:date:from:mime-version:from:to:cc:subject:date:message-id;\r\n" +
        "        bh=47DEQpj8HBSa+/TImW+5JCeuQeRkm5NMpJWZG3hSuFU=; b=abc\r\n         def";

    [Fact]
    public void TheSigningDomainAndSignedHeaders_AreRead()
    {
        var signature = DkimSignature.Parse(Gmail);

        Assert.Equal("gmail.com", signature.Domain);
        Assert.Contains("from", signature.SignedHeaders);
        Assert.False(signature.LimitsBodyLength);
        Assert.True(signature.CanVouchForSender);
    }

    /// <summary>
    /// <b>A tag twice makes the header unreadable.</b> Choosing one <c>d=</c> here while the verifier chose the
    /// other would align one domain and verify against another's key.
    /// </summary>
    [Fact]
    public void ADomainNamedTwice_IsNotReadAtAll()
    {
        Assert.Null(DkimSignature.Parse("v=1; d=example.com; d=attacker.org; h=from; bh=x; b=y"));
    }

    /// <summary>
    /// <b>A signature over part of the body proves nothing about the rest.</b> With <c>l=</c>, anybody holding
    /// one signed mail can append their own text below the signed part and it still verifies.
    /// </summary>
    [Fact]
    public void ASignatureOverPartOfTheBody_CannotVouch()
    {
        var signature = DkimSignature.Parse("v=1; d=example.com; h=from:subject; l=120; bh=x; b=y");

        Assert.True(signature.LimitsBodyLength);
        Assert.False(signature.CanVouchForSender);
    }

    [Fact]
    public void ASignatureThatDoesNotCoverFrom_CannotVouch()
    {
        Assert.False(DkimSignature.Parse("v=1; d=example.com; h=subject:date; bh=x; b=y").CanVouchForSender);
    }

    [Theory]
    [InlineData("")]
    [InlineData("v=1; h=from; bh=x; b=y")]
    [InlineData("v=1; d=example.com; bh=x; b=y")]
    [InlineData("v=1; d=; h=from")]
    [InlineData("garbage")]
    public void AHeaderWithoutADomainOrHeaderList_IsNotRead(string value)
    {
        Assert.Null(DkimSignature.Parse(value));
    }
}
