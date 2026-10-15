using Tharga.Team.Email.Mailbox;

namespace Tharga.Team.Email.Tests.Mailbox;

public class MailRouteTests
{
    private static readonly TeamMailboxOptions Options = new() { Domain = "fortdocs.app", Suffix = "-case" };
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TeamMailKeyRecord Primary = new() { TeamKey = "team-a", Key = "primarykey01", Role = MailKeyRole.Primary, CreatedAt = Now.AddDays(-10), RetiresAt = null };
    private static readonly TeamMailKeyRecord Retiring = new() { TeamKey = "team-a", Key = "secondary001", Role = MailKeyRole.Secondary, CreatedAt = Now.AddDays(-100), RetiresAt = Now.AddDays(30) };
    private static readonly TeamMailKeyRecord Retired = new() { TeamKey = "team-a", Key = "retiredkey01", Role = MailKeyRole.Secondary, CreatedAt = Now.AddDays(-200), RetiresAt = Now.AddDays(-1) };

    private static TeamMailKeyRecord Lookup(string key) => new[] { Primary, Retiring, Retired }.FirstOrDefault(x => x.Key == key);

    private static MailRouteDecision Route(params string[] deliveredTo) => MailRoute.Decide(deliveredTo, Options, Lookup, Now);

    [Fact]
    public void MailToThePrimaryKey_GoesToItsTeam()
    {
        var decision = Route("primarykey01-case@fortdocs.app");

        Assert.Equal("team-a", decision.TeamKey);
        Assert.Equal(MailRouteReason.PrimaryKey, decision.Reason);
        Assert.Null(decision.KeyRetiresAt);
    }

    [Fact]
    public void MailToARetiringKey_StillGoesToItsTeam_MarkedWithTheRetirementDate()
    {
        var decision = Route("secondary001-case@fortdocs.app");

        Assert.Equal("team-a", decision.TeamKey);
        Assert.Equal(MailRouteReason.RetiringKey, decision.Reason);
        Assert.Equal(Now.AddDays(30), decision.KeyRetiresAt);
    }

    [Fact]
    public void MailToARetiredKey_GoesToThePostmaster_NeverToTheFormerTeam()
    {
        var decision = Route("retiredkey01-case@fortdocs.app");

        Assert.Null(decision.TeamKey);
        Assert.Equal(MailRouteReason.RetiredKey, decision.Reason);
    }

    [Fact]
    public void MailToAKeyNobodyHolds_GoesToThePostmaster()
    {
        var decision = Route("nosuchkey000-case@fortdocs.app");

        Assert.Null(decision.TeamKey);
        Assert.Equal(MailRouteReason.UnknownAddress, decision.Reason);
    }

    [Fact]
    public void MailWithNoAddressOfOurs_GoesToThePostmaster_WithTheFirstRecipientRecorded()
    {
        var decision = Route("someone@elsewhere.se");

        Assert.Null(decision.TeamKey);
        Assert.Equal(MailRouteReason.UnknownAddress, decision.Reason);
        Assert.Equal("someone@elsewhere.se", decision.DeliveredTo);
    }

    [Fact]
    public void TheFirstDeliveryAddressThatIsOurs_Decides()
    {
        var decision = Route("forwarder@elsewhere.se", "primarykey01-case+item42@fortdocs.app", "secondary001-case@fortdocs.app");

        Assert.Equal(MailRouteReason.PrimaryKey, decision.Reason);
        Assert.Equal("item42", decision.ItemKey);
        Assert.Equal("primarykey01-case+item42@fortdocs.app", decision.DeliveredTo);
    }

    [Fact]
    public void AnItemAddress_CarriesTheItemToTheTeam()
    {
        Assert.Equal("0f8d4e2a91c7", Route("primarykey01-case+0f8d4e2a91c7@fortdocs.app").ItemKey);
    }

    [Fact]
    public void ARetiringKeyOnItsLastMoment_IsAlreadyRetired()
    {
        var decision = MailRoute.Decide(["secondary001-case@fortdocs.app"], Options, Lookup, Now.AddDays(30));

        Assert.Equal(MailRouteReason.RetiredKey, decision.Reason);
    }

    [Fact]
    public void NoRecipientAtAll_GoesToThePostmaster()
    {
        var decision = Route();

        Assert.Null(decision.TeamKey);
        Assert.Equal(MailRouteReason.UnknownAddress, decision.Reason);
        Assert.Null(decision.DeliveredTo);
    }
}
