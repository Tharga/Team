using Microsoft.Extensions.Options;
using Tharga.Team.Email.Mailbox;

namespace Tharga.Team.Email.Tests.Mailbox;

public class TeamMailboxKeyTests
{
    private const string TeamKey = "team-a";

    private readonly InMemoryTeamMailKeyStore _store = new();
    private readonly SteppingTimeProvider _time = new(new DateTime(2026, 10, 15, 12, 0, 0, DateTimeKind.Utc));
    private readonly TeamMailboxOptions _options = new() { Domain = "fortdocs.app", Suffix = "-case" };

    private TeamMailboxService Service(Func<int, string> generate = null) => new(_store, new InMemoryTeamMailStore(), new TeamMailNotifier(), Substitute.For<Tharga.Team.Service.ITeamPrincipalAccessor>(), Options.Create(_options), _time, null, generate);

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    [Fact]
    public async Task ATeamWithNoKey_HasNoAddress()
    {
        var addresses = await Service().GetAddressesAsync(TeamKey);

        Assert.Equal(TeamKey, addresses.TeamKey);
        Assert.Null(addresses.Primary);
        Assert.Null(addresses.Secondary);
    }

    [Fact]
    public async Task EnsuringAKey_IssuesAPrimary_WithItsAddress()
    {
        var addresses = await Service().EnsureKeyAsync(TeamKey);

        Assert.Matches("^[a-z0-9]{12}$", addresses.Primary.Key);
        Assert.Equal($"{addresses.Primary.Key}-case@fortdocs.app", addresses.Primary.Address);
        Assert.Equal(MailKeyRole.Primary, addresses.Primary.Role);
        Assert.Equal(Now, addresses.Primary.CreatedAt);
        Assert.DoesNotContain(TeamKey, addresses.Primary.Address);
    }

    [Fact]
    public async Task EnsuringAgain_KeepsTheKeyTheTeamHas()
    {
        var first = await Service().EnsureKeyAsync(TeamKey);
        var second = await Service().EnsureKeyAsync(TeamKey);

        Assert.Equal(first.Primary.Key, second.Primary.Key);
        Assert.Single(_store.All);
    }

    [Fact]
    public async Task WhenAnotherInstanceIssuedTheKeyFirst_ItsKeyIsTheOneReturned()
    {
        _store.RefuseNextIssue = true;
        _store.Add(new TeamMailKeyRecord { TeamKey = TeamKey, Key = "issuedbyother", Role = MailKeyRole.Primary, CreatedAt = Now });

        var addresses = await Service().EnsureKeyAsync(TeamKey);

        Assert.Equal("issuedbyother", addresses.Primary.Key);
    }

    [Fact]
    public async Task AKeyAnotherTeamAlreadyHolds_IsNeverIssuedAgain()
    {
        _store.Add(new TeamMailKeyRecord { TeamKey = "team-b", Key = "takenkey0000", Role = MailKeyRole.Primary, CreatedAt = Now });
        var candidates = new Queue<string>(["takenkey0000", "freshkey0000"]);

        var addresses = await Service(_ => candidates.Dequeue()).EnsureKeyAsync(TeamKey);

        Assert.Equal("freshkey0000", addresses.Primary.Key);
    }

    [Fact]
    public async Task Rotating_MakesANewPrimary_AndKeepsTheOldOneAsSecondaryUntilItRetires()
    {
        var before = await Service().EnsureKeyAsync(TeamKey);
        _time.Advance(TimeSpan.FromDays(1));

        var after = await Service().RotateKeyAsync(TeamKey);

        Assert.NotEqual(before.Primary.Key, after.Primary.Key);
        Assert.Equal(before.Primary.Key, after.Secondary.Key);
        Assert.Equal(MailKeyRole.Secondary, after.Secondary.Role);
        Assert.Equal(Now.AddDays(90), after.Secondary.RetiresAt);
    }

    [Fact]
    public async Task RotatingTwice_RetiresTheOldestAtOnce()
    {
        var first = await Service().EnsureKeyAsync(TeamKey);
        await Service().RotateKeyAsync(TeamKey);

        var after = await Service().RotateKeyAsync(TeamKey);

        Assert.NotEqual(first.Primary.Key, after.Secondary.Key);
        Assert.True(_store.All.Single(x => x.Key == first.Primary.Key).HasRetired(Now));
        Assert.Equal(3, _store.All.Count);
    }

    [Fact]
    public async Task ASecondaryPastItsRetirement_IsNoLongerShown()
    {
        await Service().EnsureKeyAsync(TeamKey);
        await Service().RotateKeyAsync(TeamKey);
        _time.Advance(TimeSpan.FromDays(90));

        var addresses = await Service().GetAddressesAsync(TeamKey);

        Assert.NotNull(addresses.Primary);
        Assert.Null(addresses.Secondary);
    }

    [Fact]
    public async Task RotatingATeamWithNoKey_IssuesItsFirst()
    {
        var addresses = await Service().RotateKeyAsync(TeamKey);

        Assert.NotNull(addresses.Primary);
        Assert.Null(addresses.Secondary);
    }

    [Fact]
    public async Task TheRetirementPeriod_IsTheHosts()
    {
        _options.RetirementPeriod = TimeSpan.FromDays(14);
        await Service().EnsureKeyAsync(TeamKey);

        var after = await Service().RotateKeyAsync(TeamKey);

        Assert.Equal(Now.AddDays(14), after.Secondary.RetiresAt);
    }

    [Fact]
    public async Task AnItemAddress_IsOnThePrimaryKey()
    {
        var addresses = await Service().EnsureKeyAsync(TeamKey);

        Assert.Equal($"{addresses.Primary.Key}-case+0f8d4e2a91c7@fortdocs.app", await Service().GetItemAddressAsync(TeamKey, "0f8d4e2a91c7"));
    }

    [Fact]
    public async Task ATeamWithNoKey_HasNoItemAddress()
    {
        Assert.Null(await Service().GetItemAddressAsync(TeamKey, "0f8d4e2a91c7"));
    }
}
