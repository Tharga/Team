using Microsoft.Extensions.DependencyInjection;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// Who a support mail's sender is: every account holding the address, and only the memberships that grant
/// access.
/// </summary>
/// <remarks>
/// <b>The exclusions are what earn this file its place.</b> A pending invitation, a suspended membership, a
/// deleted team and a disabled account each have to stay out, because the answer can put a mail inside a team.
/// The repository's own query already filters some of these; the directory is asserted against a repository
/// that does not, so it holds whatever a host's repository returns.
/// </remarks>
public class MongoSupportSenderDirectoryTests
{
    private const string Address = "anna@example.com";

    private readonly IUserRepository<UserEntity> _users = Substitute.For<IUserRepository<UserEntity>>();
    private readonly ITeamRepository<TeamEntity, Member> _teams = Substitute.For<ITeamRepository<TeamEntity, Member>>();

    private MongoSupportSenderDirectory<UserEntity, TeamEntity, Member> Directory => new(_users, _teams);

    [Fact]
    public async Task AnActiveMember_IsFoundWithTheirTeam()
    {
        Users(User("u-1", "sub-1", name: "Anna Andersson"));
        Teams("u-1", Team("acme", "Acme", Membership("u-1")));

        var sender = Assert.Single(await Directory.FindAsync(Address, TestContext.Current.CancellationToken));

        Assert.Equal("sub-1", sender.Identity);
        Assert.Equal("Anna Andersson", sender.Name);
        var team = Assert.Single(sender.Teams);
        Assert.Equal("acme", team.Key);
        Assert.Equal("Acme", team.Name);
    }

    [Fact]
    public async Task TheAddressIsLookedUpTrimmed()
    {
        Users(User("u-1", "sub-1"));

        await Directory.FindAsync("  anna@example.com ", TestContext.Current.CancellationToken);

        await _users.Received(1).GetByEMailAsync(Address);
    }

    [Fact]
    public async Task APendingInvitation_IsNoMembership()
    {
        Users(User("u-1", "sub-1"));
        Teams("u-1", Team("acme", "Acme", Membership("u-1", MembershipState.Invited)));

        Assert.Empty(Assert.Single(await Directory.FindAsync(Address, TestContext.Current.CancellationToken)).Teams);
    }

    /// <summary>
    /// <b>Suspension is a containment</b>, and mail must not become the way a suspended member still writes into
    /// the team — not by opening a case there, and not by staff being offered it.
    /// </summary>
    [Fact]
    public async Task ASuspendedMembership_IsLeftOut()
    {
        Users(User("u-1", "sub-1"));
        Teams("u-1",
            Team("acme", "Acme", Membership("u-1") with { SuspendedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) }),
            Team("beta", "Beta", Membership("u-1")));

        var sender = Assert.Single(await Directory.FindAsync(Address, TestContext.Current.CancellationToken));

        Assert.Equal(["beta"], sender.Teams.Select(x => x.Key));
    }

    /// <summary>A membership with no access level grants nothing since 4.0, so mail must not place a case in that team either.</summary>
    [Fact]
    public async Task AMembershipWithNoAccessLevel_IsLeftOut()
    {
        Users(User("u-1", "sub-1"));
        Teams("u-1",
            Team("acme", "Acme", Membership("u-1") with { AccessLevel = null }),
            Team("beta", "Beta", Membership("u-1")));

        var sender = Assert.Single(await Directory.FindAsync(Address, TestContext.Current.CancellationToken));

        Assert.Equal(["beta"], sender.Teams.Select(x => x.Key));
    }

    [Fact]
    public async Task ADeletedTeam_IsLeftOut()
    {
        Users(User("u-1", "sub-1"));
        Teams("u-1", Team("acme", "Acme", Membership("u-1")) with { DeletedAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) });

        Assert.Empty(Assert.Single(await Directory.FindAsync(Address, TestContext.Current.CancellationToken)).Teams);
    }

    /// <summary>
    /// <b>Returned, but with no teams.</b> A disabled account cannot sign in, so it must not gain a way in by
    /// mail; it still holds the address, though, and an address two accounts hold belongs to neither.
    /// </summary>
    [Fact]
    public async Task ADisabledAccount_IsReturnedWithoutTeams()
    {
        Users(User("u-1", "sub-1", disabled: true));
        Teams("u-1", Team("acme", "Acme", Membership("u-1")));

        var sender = Assert.Single(await Directory.FindAsync(Address, TestContext.Current.CancellationToken));

        Assert.Equal("sub-1", sender.Identity);
        Assert.Empty(sender.Teams);
    }

    [Fact]
    public async Task EveryAccountHoldingTheAddress_IsReturned()
    {
        Users(User("u-1", "sub-1"), User("u-2", "sub-2"));
        Teams("u-1", Team("acme", "Acme", Membership("u-1")));
        Teams("u-2", Team("beta", "Beta", Membership("u-2")));

        var senders = await Directory.FindAsync(Address, TestContext.Current.CancellationToken);

        Assert.Equal(["sub-1", "sub-2"], senders.Select(x => x.Identity));
    }

    [Fact]
    public async Task AnAddressNobodyHolds_FindsNobody()
    {
        Users();

        Assert.Empty(await Directory.FindAsync(Address, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ABlankAddress_IsNotLookedUp()
    {
        Assert.Empty(await Directory.FindAsync("  ", TestContext.Current.CancellationToken));

        await _users.DidNotReceive().GetByEMailAsync(Arg.Any<string>());
    }

    /// <summary>Hosts get it from the registration they already make, with no call of their own.</summary>
    [Fact]
    public void WithBothRepositories_TheDirectoryIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamRepository(o =>
        {
            o.RegisterUserRepository<UserEntity>();
            o.RegisterTeamRepository<TeamEntity, Member>();
        });

        var descriptor = services.Single(x => x.ServiceType == typeof(ISupportSenderDirectory));

        Assert.Equal(typeof(MongoSupportSenderDirectory<UserEntity, TeamEntity, Member>), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    /// <summary>Without both stores there is nobody to look up, and support mail opens unassigned as it always did.</summary>
    [Fact]
    public void WithOnlyTheTeamRepository_NoDirectoryIsRegistered()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamRepository(o => o.RegisterTeamRepository<TeamEntity, Member>());

        Assert.DoesNotContain(services, x => x.ServiceType == typeof(ISupportSenderDirectory));
    }

    [Fact]
    public void AHostsOwnDirectory_Wins()
    {
        var services = new ServiceCollection();
        services.AddScoped<ISupportSenderDirectory>(_ => Substitute.For<ISupportSenderDirectory>());
        services.AddThargaTeamRepository(o =>
        {
            o.RegisterUserRepository<UserEntity>();
            o.RegisterTeamRepository<TeamEntity, Member>();
        });

        Assert.Null(services.Single(x => x.ServiceType == typeof(ISupportSenderDirectory)).ImplementationType);
    }

    private void Users(params UserEntity[] users) => _users.GetByEMailAsync(Address).Returns(users);

    private void Teams(string userKey, params TeamEntity[] teams) => _teams.GetTeamsByUserAsync(userKey).Returns(teams.ToAsyncEnumerable());

    private static UserEntity User(string key, string identity, string name = null, bool disabled = false) => new()
    {
        Key = key,
        Identity = identity,
        EMail = Address,
        Name = name,
        DisabledAt = disabled ? new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) : null
    };

    private static TeamEntity Team(string key, string name, params Member[] members) => new() { Key = key, Name = name, Members = members };

    private static Member Membership(string key, MembershipState state = MembershipState.Member) => new() { Key = key, State = state, AccessLevel = AccessLevel.User };

    public record UserEntity : EntityBase, IUser
    {
        public string Key { get; init; }
        public string Identity { get; init; }
        public string EMail { get; init; }
        public string Name { get; init; }
        public DateTime? DisabledAt { get; init; }
    }

    public record Member : TeamMemberBase;

    public record TeamEntity : TeamEntityBase<Member>;
}
