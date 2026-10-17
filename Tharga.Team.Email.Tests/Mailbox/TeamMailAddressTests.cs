using Tharga.Team.Email.Mailbox;

namespace Tharga.Team.Email.Tests.Mailbox;

public class TeamMailAddressTests
{
    private static readonly TeamMailboxOptions Options = new() { Domain = "fortdocs.app", Suffix = "-case" };

    [Fact]
    public void TheTeamAddress_IsTheKeySuffixAndDomain()
    {
        Assert.Equal("8fjgthitwne3-case@fortdocs.app", TeamMailAddress.For("8fjgthitwne3", Options));
    }

    [Fact]
    public void AnItemAddress_AddsTheItemAfterAPlus()
    {
        Assert.Equal("8fjgthitwne3-case+0f8d4e2a91c7@fortdocs.app", TeamMailAddress.ForItem("8fjgthitwne3", "0f8d4e2a91c7", Options));
    }

    [Theory]
    [InlineData("a+b")]
    [InlineData("a@b")]
    [InlineData("a b")]
    [InlineData("")]
    public void AnItemKeyThatWouldBreakTheAddress_IsRefused(string itemKey)
    {
        Assert.Throws<ArgumentException>(() => TeamMailAddress.ForItem("8fjgthitwne3", itemKey, Options));
    }

    [Theory]
    [InlineData("8fjgthitwne3-case@fortdocs.app", "8fjgthitwne3", null)]
    [InlineData("8FJGTHITWNE3-Case@FortDocs.App", "8fjgthitwne3", null)]
    [InlineData("8fjgthitwne3-case+0f8d4e2a91c7@fortdocs.app", "8fjgthitwne3", "0f8d4e2a91c7")]
    [InlineData("<8fjgthitwne3-case@fortdocs.app>", "8fjgthitwne3", null)]
    public void AnAddressOfOurs_ReadsBackItsKeyAndItem(string address, string key, string itemKey)
    {
        var parsed = TeamMailAddress.Parse(address, Options);

        Assert.NotNull(parsed);
        Assert.Equal(key, parsed.Key);
        Assert.Equal(itemKey, parsed.ItemKey);
    }

    [Theory]
    [InlineData("8fjgthitwne3-case@elsewhere.se")]
    [InlineData("8fjgthitwne3@fortdocs.app")]
    [InlineData("support@fortdocs.app")]
    [InlineData("-case@fortdocs.app")]
    [InlineData("8fjg.thitwne3-case@fortdocs.app")]
    [InlineData("not an address")]
    [InlineData(null)]
    public void AnAddressThatIsNotOurs_ReadsAsNothing(string address)
    {
        Assert.Null(TeamMailAddress.Parse(address, Options));
    }

    [Fact]
    public void WithNoSuffix_TheKeyIsTheWholeLocalPart()
    {
        var options = new TeamMailboxOptions { Domain = "example.org" };

        Assert.Equal("8fjgthitwne3@example.org", TeamMailAddress.For("8fjgthitwne3", options));
        Assert.Equal("8fjgthitwne3", TeamMailAddress.Parse("8fjgthitwne3+x1@example.org", options)?.Key);
    }

    [Fact]
    public void WithNoDomain_NoAddressCanBeMade()
    {
        Assert.Throws<InvalidOperationException>(() => TeamMailAddress.For("8fjgthitwne3", new TeamMailboxOptions()));
    }
}
