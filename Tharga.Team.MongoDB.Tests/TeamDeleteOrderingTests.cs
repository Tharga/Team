using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// <see cref="TeamServiceRepositoryBase{TTeamEntity,TMember}"/> removes the team record <b>before</b>
/// dropping the team's database, and turns a drop failure into a <see cref="TeamStorageException"/>.
/// </summary>
/// <remarks>
/// <b>Reported by Eplicta FortDocs, Tharga/Team#224.</b> Deleting a team threw a raw
/// <c>MongoCommandException</c> into the UI because their CI database user may not drop databases, and the
/// drop ran <i>first</i>.
/// <para>
/// The ordering is the half that matters even when nothing throws in front of a user. The two writes cannot
/// be atomic — one is a document delete, the other a database drop — so the only choice is which way a
/// partial failure fails. Dropping first and then failing to delete the record leaves a live team pointing
/// at deleted data: it lists, resolves and authorizes, and every read returns empty. That is silent.
/// Deleting the record first leaves an orphaned database, which is inert.
/// </para>
/// <para>
/// FortDocs were lucky — the drop threw before anything changed — so this was reported as an error-handling
/// problem. It is the ordering that would have lost data.
/// </para>
/// </remarks>
public class TeamDeleteOrderingTests
{
    private const string TeamKey = "team-1";

    public record TestTeamEntity : TeamEntityBase<TestMember>;

    public record TestMember : TeamMemberBase;

    /// <summary>Records the order the two writes actually happened in.</summary>
    private sealed class Journal
    {
        private readonly List<string> _steps = [];

        public void Record(string step) => _steps.Add(step);

        public IReadOnlyList<string> Steps => _steps;
    }

    private sealed class TestTeamService(
        IUserService userService,
        ITeamRepository<TestTeamEntity, TestMember> teamRepository,
        IMongoDbServiceFactory factory)
        : TeamServiceRepositoryBase<TestTeamEntity, TestMember>(userService, teamRepository, factory)
    {
        protected override Task<TestTeamEntity> CreateTeam(string teamKey, string name, IUser user, string displayName)
            => Task.FromResult(new TestTeamEntity { Key = teamKey, Name = name });

        protected override Task<TestMember> CreateTeamMember(InviteUserModel model)
            => Task.FromResult(new TestMember());

        public Task DeleteAsync(string teamKey) => base.DeleteTeamAsync(teamKey);
    }

    private static (TestTeamService Sut, Journal Journal, ITeamRepository<TestTeamEntity, TestMember> Repository)
        Build(bool dropThrows)
    {
        var journal = new Journal();

        var repository = Substitute.For<ITeamRepository<TestTeamEntity, TestMember>>();
        repository.DeleteAsync(Arg.Any<string>()).Returns(_ =>
        {
            journal.Record("record");
            return Task.CompletedTask;
        });

        var mongoDbService = Substitute.For<IMongoDbService>();
        mongoDbService.GetDatabaseName().Returns("db-" + TeamKey);
        mongoDbService.When(x => x.DropDatabase(Arg.Any<string>())).Do(_ =>
        {
            journal.Record("database");
            if (dropThrows) throw new InvalidOperationException("user is not allowed to do action [dropDatabase]");
        });

        var factory = Substitute.For<IMongoDbServiceFactory>();
        factory.GetMongoDbService(Arg.Any<Func<DatabaseContext>>()).Returns(mongoDbService);

        return (new TestTeamService(Substitute.For<IUserService>(), repository, factory), journal, repository);
    }

    /// <summary>The ordering, on the happy path.</summary>
    [Fact]
    public async Task DeleteTeam_RemovesTheRecordBeforeDroppingTheDatabase()
    {
        var (sut, journal, _) = Build(dropThrows: false);

        await sut.DeleteAsync(TeamKey);

        Assert.Equal(["record", "database"], journal.Steps);
    }

    /// <summary>
    /// <b>The failure this ordering exists to prevent.</b> When the drop fails, the record must already be
    /// gone — so what survives is an orphaned database rather than a team whose data has been deleted
    /// underneath it.
    /// </summary>
    [Fact]
    public async Task DeleteTeam_WhenTheDropFails_TheRecordIsAlreadyGone()
    {
        var (sut, journal, repository) = Build(dropThrows: true);

        await Assert.ThrowsAsync<TeamStorageException>(() => sut.DeleteAsync(TeamKey));

        await repository.Received(1).DeleteAsync(TeamKey);
        Assert.Equal(["record", "database"], journal.Steps);
    }

    /// <summary>
    /// A store refusal is wrapped, not propagated. The driver exception reached the error page as a stack
    /// trace; what an operator needs is what the deployment has to grant.
    /// </summary>
    [Fact]
    public async Task DeleteTeam_WrapsAStoreRefusalWithSomethingAnOperatorCanAct_On()
    {
        var (sut, _, _) = Build(dropThrows: true);

        var ex = await Assert.ThrowsAsync<TeamStorageException>(() => sut.DeleteAsync(TeamKey));

        Assert.Equal(TeamKey, ex.TeamKey);
        Assert.Contains(TeamKey, ex.Message);
        Assert.Contains("dropDatabase", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(ex.InnerException);
    }

    /// <summary>
    /// The self-check: the journal would record the same single entry whichever order the code ran in if
    /// only one write happened, so this proves both were observed.
    /// </summary>
    [Fact]
    public async Task BothWritesAreObserved()
    {
        var (sut, journal, _) = Build(dropThrows: false);

        await sut.DeleteAsync(TeamKey);

        Assert.Equal(2, journal.Steps.Count);
        Assert.Contains("record", journal.Steps);
        Assert.Contains("database", journal.Steps);
    }
}
