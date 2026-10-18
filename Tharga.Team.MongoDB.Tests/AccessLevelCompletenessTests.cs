using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// Finding members that carry no stored access level. Since 4.0 they read as null and are granted nothing;
/// before it they were treated as <see cref="AccessLevel.Owner"/>.
/// </summary>
/// <remarks>
/// <b>These tests are database-free on purpose, and the argument they make is in three parts.</b> There is
/// no way to assert "this query returns the right rows" without a live server, so instead:
/// <list type="number">
/// <item>the filter renders to exactly <c>$exists: false</c> on the member's access level;</item>
/// <item>a member stored at <i>any</i> level, <see cref="AccessLevel.Owner"/> included, always writes the
/// field — so no correctly-stored member can ever match it;</item>
/// <item>a document written before the field existed genuinely lacks it — so those, and only those,
/// match.</item>
/// </list>
/// Together those pin the behaviour more precisely than a round-trip against a server would, because they
/// name <i>why</i> each case falls where it does.
/// <para>
/// <b>The second one is the test that earns its place.</b> Before 4.0 a missing field and a stored
/// <c>Owner</c> were indistinguishable once deserialized, which is why the check reads the store rather than
/// the members. Since 4.0 they deserialize apart (null versus Owner), but the store query stays: it finds the
/// affected members without loading every team.
/// </para>
/// </remarks>
public class AccessLevelCompletenessTests
{
    private sealed record TestMember : TeamMemberBase;

    private sealed record TestTeam : TeamEntityBase<TestMember>;

    private const string AccessLevelField = "AccessLevel";

    /// <summary>
    /// The query is on field <i>presence</i>, which is the one thing a LINQ predicate cannot express — so
    /// this also documents why the typed <c>GetProjectionAsync</c> overload is not used.
    /// </summary>
    [Fact]
    public void TheFilter_MatchesOnTheAbsenceOfTheField()
    {
        var filter = AccessLevelCompleteness.MembersWithNoAccessLevel<TestTeam, TestMember>();

        var rendered = filter.Render(new RenderArgs<TestTeam>(
            BsonSerializer.SerializerRegistry.GetSerializer<TestTeam>(),
            BsonSerializer.SerializerRegistry));

        var expected = BsonDocument.Parse(
            """{ "Members" : { "$elemMatch" : { "AccessLevel" : { "$exists" : false } } } }""");

        Assert.Equal(expected, rendered);
    }

    /// <summary>
    /// The discrimination that makes the check safe to ship: a correctly stored member always writes the
    /// field, so it can never satisfy <c>$exists: false</c> — including one deliberately stored as Owner.
    /// </summary>
    [Theory]
    [InlineData(AccessLevel.Owner)]
    [InlineData(AccessLevel.Administrator)]
    [InlineData(AccessLevel.User)]
    [InlineData(AccessLevel.Viewer)]
    [InlineData(AccessLevel.Custom)]
    public void AMemberStoredAtAnyLevel_WritesTheField(AccessLevel level)
    {
        var document = new TestMember { Key = "member-1", AccessLevel = level }.ToBsonDocument();

        Assert.True(document.Contains(AccessLevelField));
    }

    /// <summary>
    /// The condition being hunted: a document written before the field existed. Built as raw BSON, because
    /// constructing the record and omitting the property would still <i>serialize</i> a value.
    /// </summary>
    [Fact]
    public void AMemberDocumentWrittenBeforeTheFieldExisted_LacksIt()
    {
        var legacy = new BsonDocument { { "Key", "member-1" }, { "Name", "Written long ago" } };

        Assert.False(legacy.Contains(AccessLevelField));

        var deserialized = BsonSerializer.Deserialize<TestMember>(legacy);
        Assert.Null(deserialized.AccessLevel);
    }

    /// <summary>
    /// The trap that made this check necessary, now closed: before 4.0 the two cases deserialized to the
    /// same value, so nothing downstream of the store could tell them apart. Since 4.0 they do not.
    /// </summary>
    [Fact]
    public void OnceDeserialized_AMissingLevelIsDistinguishableFromAStoredOwner()
    {
        var missing = BsonSerializer.Deserialize<TestMember>(
            new BsonDocument { { "Key", "member-1" } });

        var storedOwner = BsonSerializer.Deserialize<TestMember>(
            new BsonDocument { { "Key", "member-2" }, { AccessLevelField, "Owner" } });

        Assert.Null(missing.AccessLevel);
        Assert.Equal(AccessLevel.Owner, storedOwner.AccessLevel);
    }

    /// <summary>
    /// A diagnostic must never be the reason an application fails to start, so every path out of
    /// <c>StartAsync</c> is a return.
    /// </summary>
    [Fact]
    public async Task TheCheck_DoesNotThrow_WhenThereIsNoCollectionToRead()
    {
        var check = new AccessLevelCompletenessCheck<TestTeam, TestMember>(
            new ServiceCollection().BuildServiceProvider());

        var exception = await Record.ExceptionAsync(() => check.StartAsync(TestContext.Current.CancellationToken));

        Assert.Null(exception);
    }

    [Fact]
    public void TheCheck_IsRegistered_AlongsideATeamRepository()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamRepository(o => o.RegisterTeamRepository<TestTeam, TestMember>());

        Assert.Contains(services, s =>
            s.ServiceType == typeof(IHostedService) &&
            s.ImplementationType == typeof(AccessLevelCompletenessCheck<TestTeam, TestMember>));
    }

    /// <summary>
    /// Turning it off must remove the registration outright, not merely make it return early — an opted-out
    /// host should not pay for a hosted service at all.
    /// </summary>
    [Fact]
    public void TheCheck_IsNotRegistered_WhenTurnedOff()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamRepository(o =>
        {
            o.CheckMemberAccessLevels = false;
            o.RegisterTeamRepository<TestTeam, TestMember>();
        });

        Assert.DoesNotContain(services, s =>
            s.ImplementationType == typeof(AccessLevelCompletenessCheck<TestTeam, TestMember>));
    }

    /// <summary>
    /// Without a team repository there is nothing to read, so the check should not exist either.
    /// </summary>
    [Fact]
    public void TheCheck_IsNotRegistered_WithoutATeamRepository()
    {
        var services = new ServiceCollection();
        services.AddThargaTeamRepository();

        Assert.DoesNotContain(services, s => s.ServiceType == typeof(IHostedService));
    }

    [Fact]
    public void TheOption_IsOnByDefault()
    {
        Assert.True(new ThargaTeamOptions().CheckMemberAccessLevels);
    }
}
