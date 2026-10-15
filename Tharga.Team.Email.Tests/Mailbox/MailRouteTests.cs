using Tharga.Team.Email.Mailbox;

namespace Tharga.Team.Email.Tests.Mailbox;

public class MailRouteTests
{
    private static readonly TeamMailboxOptions Options = new() { Domain = "fortdocs.app", Suffix = "-case" };
    private static readonly DateTime Now = new(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc);

    private static readonly TeamMailKeyRecord Primary = new() { TeamKey = "team-a", Key = "primarykey01", Role = MailKeyRole.Primary, CreatedAt = Now.AddDays(-10), RetiresAt = null };
    private static readonly TeamMailKeyRecord Retiring = new() { TeamKey = "team-a", Key = "secondary001", Role = MailKeyRole.Secondary, CreatedAt = Now.AddDays(-100), RetiresAt = Now.AddDays(30) };
    private static readonly TeamMailKeyRecord Retired = new() { TeamKey = "team-a", Key = "retiredkey01", Role = MailKeyRole.Secondary, CreatedAt = Now.AddDays(-200), RetiresAt = Now.AddDays(-1) };

    private static Task<TeamMailKeyRecord> Lookup(string key) => Task.FromResult(new[] { Primary, Retiring, Retired }.FirstOrDefault(x => x.Key == key));

    private static Task<MailRouteDecision> Route(params string[] deliveredTo) => MailRoute.DecideAsync(deliveredTo, Options, Lookup, Now);

    [Fact]
    public async Task MailToThePrimaryKey_GoesToItsTeam()
    {
        var decision = await Route("primarykey01-case@fortdocs.app");

        Assert.Equal("team-a", decision.TeamKey);
        Assert.Equal(MailRouteReason.PrimaryKey, decision.Reason);
        Assert.Null(decision.KeyRetiresAt);
    }

    [Fact]
    public async Task MailToARetiringKey_StillGoesToItsTeam_MarkedWithTheRetirementDate()
    {
        var decision = await Route("secondary001-case@fortdocs.app");

        Assert.Equal("team-a", decision.TeamKey);
        Assert.Equal(MailRouteReason.RetiringKey, decision.Reason);
        Assert.Equal(Now.AddDays(30), decision.KeyRetiresAt);
    }

    [Fact]
    public async Task MailToARetiredKey_GoesToThePostmaster_NeverToTheFormerTeam()
    {
        var decision = await Route("retiredkey01-case@fortdocs.app");

        Assert.Null(decision.TeamKey);
        Assert.Equal(MailRouteReason.RetiredKey, decision.Reason);
    }

    [Fact]
    public async Task MailToAKeyNobodyHolds_GoesToThePostmaster()
    {
        var decision = await Route("nosuchkey000-case@fortdocs.app");

        Assert.Null(decision.TeamKey);
        Assert.Equal(MailRouteReason.UnknownAddress, decision.Reason);
    }

    [Fact]
    public async Task MailWithNoAddressOfOurs_GoesToThePostmaster_WithTheFirstRecipientRecorded()
    {
        var decision = await Route("someone@elsewhere.se");

        Assert.Null(decision.TeamKey);
        Assert.Equal(MailRouteReason.UnknownAddress, decision.Reason);
        Assert.Equal("someone@elsewhere.se", decision.DeliveredTo);
    }

    [Fact]
    public async Task TheFirstDeliveryAddressThatIsOurs_Decides()
    {
        var decision = await Route("forwarder@elsewhere.se", "primarykey01-case+item42@fortdocs.app", "secondary001-case@fortdocs.app");

        Assert.Equal(MailRouteReason.PrimaryKey, decision.Reason);
        Assert.Equal("item42", decision.ItemKey);
        Assert.Equal("primarykey01-case+item42@fortdocs.app", decision.DeliveredTo);
    }

    [Fact]
    public async Task AnItemAddress_CarriesTheItemToTheTeam()
    {
        Assert.Equal("0f8d4e2a91c7", (await Route("primarykey01-case+0f8d4e2a91c7@fortdocs.app")).ItemKey);
    }

    [Fact]
    public async Task ARetiringKeyOnItsLastMoment_IsAlreadyRetired()
    {
        var decision = await MailRoute.DecideAsync(["secondary001-case@fortdocs.app"], Options, Lookup, Now.AddDays(30));

        Assert.Equal(MailRouteReason.RetiredKey, decision.Reason);
    }

    [Fact]
    public async Task NoRecipientAtAll_GoesToThePostmaster()
    {
        var decision = await Route();

        Assert.Null(decision.TeamKey);
        Assert.Equal(MailRouteReason.UnknownAddress, decision.Reason);
        Assert.Null(decision.DeliveredTo);
    }
}
