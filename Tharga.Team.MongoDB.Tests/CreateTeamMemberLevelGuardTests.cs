using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// A host's <c>CreateTeamMember</c> that returns a member with no access level fails the invite (4.0).
/// </summary>
/// <remarks>
/// The override is abstract, so forgetting to copy <c>model.AccessLevel</c> still compiles. Before 4.0 it
/// silently produced an Owner; since 4.0 it would produce a member who can do nothing. Both are wrong, so the
/// invite throws and names the override instead of storing either.
/// </remarks>
public class CreateTeamMemberLevelGuardTests
{
    public record TestTeamEntity : TeamEntityBase<TestMember>;

    public record TestMember : TeamMemberBase;

    private sealed class ForgetfulTeamService(ITeamRepository<TestTeamEntity, TestMember> repository)
        : TeamServiceRepositoryBase<TestTeamEntity, TestMember>(
            Substitute.For<IUserService>(), repository, Substitute.For<IMongoDbServiceFactory>())
    {
        protected override Task<TestTeamEntity> CreateTeam(string teamKey, string name, IUser user, string displayName)
            => Task.FromResult(new TestTeamEntity { Key = teamKey, Name = name });

        // The mistake: every field copied except the level.
        protected override Task<TestMember> CreateTeamMember(InviteUserModel model)
            => Task.FromResult(new TestMember { Name = model.Name, State = MembershipState.Invited });

        public Task AddAsync(string teamKey, InviteUserModel model) => base.AddTeamMemberAsync(teamKey, model);
    }

    [Fact]
    public async Task AnOverrideThatDropsTheLevel_FailsTheInvite_AndStoresNothing()
    {
        var repository = Substitute.For<ITeamRepository<TestTeamEntity, TestMember>>();
        var sut = new ForgetfulTeamService(repository);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => sut.AddAsync("team-1", new InviteUserModel { Email = "new@example.com", AccessLevel = AccessLevel.Viewer }));

        Assert.Contains("ForgetfulTeamService.CreateTeamMember", ex.Message);
        Assert.Contains("AccessLevel", ex.Message);
        Assert.Empty(repository.ReceivedCalls());
    }
}
