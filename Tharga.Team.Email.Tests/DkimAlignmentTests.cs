namespace Tharga.Team.Email.Tests;

/// <summary>
/// Which signing domains may vouch for which sender: the sender's own domain or one above it, nothing else.
/// </summary>
public class DkimAlignmentTests
{
    [Theory]
    [InlineData("example.com", "example.com")]
    [InlineData("Example.COM", "example.com")]
    [InlineData("example.com.", "example.com")]
    [InlineData("mail.example.com", "example.com")]
    [InlineData("eu.mail.example.com", "example.com")]
    public void TheSendersDomainOrAParentOfIt_Aligns(string fromDomain, string signingDomain)
    {
        Assert.True(DkimAlignment.IsAligned(fromDomain, signingDomain));
    }

    /// <summary>A valid signature from some other domain says only that the other domain relayed the mail.</summary>
    [Theory]
    [InlineData("example.com", "other.org")]
    [InlineData("example.com", "mail.example.com")]
    [InlineData("example.com", "notexample.com")]
    [InlineData("badexample.com", "example.com")]
    [InlineData("example.com", "com")]
    [InlineData("example.com", "")]
    [InlineData("", "example.com")]
    [InlineData(null, "example.com")]
    [InlineData("example.com", null)]
    public void AnythingElse_DoesNot(string fromDomain, string signingDomain)
    {
        Assert.False(DkimAlignment.IsAligned(fromDomain, signingDomain));
    }
}
