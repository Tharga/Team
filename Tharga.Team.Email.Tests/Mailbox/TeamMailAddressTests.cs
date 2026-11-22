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

    // ---- a prefix, and earlier formats ----

    private static readonly TeamMailboxOptions Prefixed = new() { Domain = "example.org", Prefix = "case-" };

    [Fact]
    public void APrefix_IsWrittenBeforeTheKey_InTheTeamAddress_AndTheItemAddress()
    {
        Assert.Equal("case-abc123@example.org", TeamMailAddress.For("abc123", Prefixed));
        Assert.Equal("case-abc123+item1@example.org", TeamMailAddress.ForItem("abc123", "item1", Prefixed));
    }

    [Fact]
    public void APrefixAndASuffix_BothSurroundTheKey()
    {
        var options = new TeamMailboxOptions { Domain = "example.org", Prefix = "a-", Suffix = "-b" };

        Assert.Equal("a-abc123-b@example.org", TeamMailAddress.For("abc123", options));
        Assert.Equal("abc123", TeamMailAddress.Parse("A-ABC123-B+x@Example.org", options)?.Key);
    }

    [Theory]
    [InlineData("case-abc123@example.org", "abc123", null)]
    [InlineData("CASE-ABC123@Example.Org", "abc123", null)]
    [InlineData("case-abc123+item1@example.org", "abc123", "item1")]
    [InlineData("<case-abc123@example.org>", "abc123", null)]
    public void AnAddressWithAPrefix_ReadsBackItsKeyAndItem(string address, string key, string itemKey)
    {
        var parsed = TeamMailAddress.Parse(address, Prefixed);

        Assert.NotNull(parsed);
        Assert.Equal(key, parsed.Key);
        Assert.Equal(itemKey, parsed.ItemKey);
    }

    [Theory]
    [InlineData("abc123@example.org")]
    [InlineData("case-@example.org")]
    [InlineData("case-abc-123@example.org")]
    [InlineData("other-abc123@example.org")]
    [InlineData("case-abc123+@example.org")]
    public void WithAPrefix_AnAddressWithoutOne_OrWithNoKey_ReadsAsNothing(string address)
    {
        Assert.Null(TeamMailAddress.Parse(address, Prefixed));
    }

    private static TeamMailboxOptions Changed() => new()
    {
        Domain = "example.org",
        Prefix = "case-",
        AcceptedFormats = [new TeamMailAddressFormat { Suffix = "-case" }, new TeamMailAddressFormat()]
    };

    /// <summary>The same key reads the same in every format, so a host can change how its addresses look without addresses already handed out going dead.</summary>
    [Theory]
    [InlineData("case-abc123@example.org", null)]
    [InlineData("abc123-case@example.org", null)]
    [InlineData("abc123@example.org", null)]
    [InlineData("case-abc123+item1@example.org", "item1")]
    [InlineData("abc123-case+item1@example.org", "item1")]
    [InlineData("ABC123-CASE+item1@EXAMPLE.ORG", "item1")]
    [InlineData("abc123+item1@example.org", "item1")]
    public void AnAcceptedEarlierFormat_RoutesToTheSameKeyAndItem(string address, string itemKey)
    {
        var parsed = TeamMailAddress.Parse(address, Changed());

        Assert.NotNull(parsed);
        Assert.Equal("abc123", parsed.Key);
        Assert.Equal(itemKey, parsed.ItemKey);
    }

    [Fact]
    public void AnAcceptedFormat_IsNeverMade_EveryAddressIsInTheConfiguredFormat()
    {
        Assert.Equal("case-abc123@example.org", TeamMailAddress.For("abc123", Changed()));
        Assert.Equal("case-abc123+item1@example.org", TeamMailAddress.ForItem("abc123", "item1", Changed()));
    }

    // The empty format in the list accepts any plain key, as an address with no prefix or suffix always did; what is not a plain key is still nothing.
    [Theory]
    [InlineData("abc123-other@example.org")]
    [InlineData("abc-123-case@example.org")]
    [InlineData("-case@example.org")]
    [InlineData("abc123-case@elsewhere.se")]
    public void AnAddressInNoFormat_ReadsAsNothing_AsBefore(string address)
    {
        Assert.Null(TeamMailAddress.Parse(address, Changed()));
    }

    /// <summary>The configured format wins where an address would read in both, though the key is the same letters and digits either way only when the formats agree.</summary>
    [Fact]
    public void TheConfiguredFormat_IsTriedBeforeTheAcceptedOnes()
    {
        var options = new TeamMailboxOptions { Domain = "example.org", Prefix = "a", AcceptedFormats = [new TeamMailAddressFormat()] };

        Assert.Equal("bc1", TeamMailAddress.Parse("abc1@example.org", options)?.Key);
    }

    [Fact]
    public void WithNoPrefixAndNoAcceptedFormats_NothingChanges()
    {
        var options = new TeamMailboxOptions { Domain = "fortdocs.app", Suffix = "-case" };

        Assert.Empty(options.AcceptedFormats);
        Assert.Equal(string.Empty, options.Prefix);
        Assert.Equal("8fjgthitwne3-case@fortdocs.app", TeamMailAddress.For("8fjgthitwne3", options));
        Assert.Equal("8fjgthitwne3", TeamMailAddress.Parse("8fjgthitwne3-case@fortdocs.app", options)?.Key);
    }

    [Fact]
    public void AFormatListWithANullInIt_IsSkippedWhenReading()
    {
        var options = new TeamMailboxOptions { Domain = "example.org", AcceptedFormats = [null, new TeamMailAddressFormat { Suffix = "-x" }] };

        Assert.Equal("abc123", TeamMailAddress.Parse("abc123-x@example.org", options)?.Key);
    }

    [Fact]
    public void WithNoDomain_NoAddressCanBeMade()
    {
        Assert.Throws<InvalidOperationException>(() => TeamMailAddress.For("8fjgthitwne3", new TeamMailboxOptions()));
    }
}
