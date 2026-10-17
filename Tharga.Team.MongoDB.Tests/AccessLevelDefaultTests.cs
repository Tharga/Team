using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Attributes;
using Tharga.Team.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// What an <see cref="AccessLevel"/> means when nobody chose one.
/// </summary>
/// <remarks>
/// Before 4.0 these tests documented the exposure: an unset level was <see cref="AccessLevel.Owner"/>, the
/// enum's zero value. Since 4.0 a member's level is <c>AccessLevel?</c> and an unset one is null, which
/// grants nothing. They now pin the fix, and fail loudly if it is ever reverted.
/// </remarks>
public class AccessLevelDefaultTests
{
    private sealed record TestMember : TeamMemberBase;

    /// <summary>
    /// The root of it, still true: <c>Owner</c> is the enum's zero value. That is why the member's level is
    /// nullable rather than defaulted, and why no sentinel was added at ordinal zero.
    /// </summary>
    [Fact]
    public void TheDefaultAccessLevel_IsOwner()
    {
        Assert.Equal(AccessLevel.Owner, default(AccessLevel));
        Assert.Equal(0, (int)AccessLevel.Owner);
    }

    /// <summary>
    /// A stored member document with no <c>AccessLevel</c> field — written before the field existed, or by
    /// any writer that omitted it — comes back with no level, not as an Owner.
    /// </summary>
    [Fact]
    public void AStoredMemberMissingTheField_DeserializesAsNull()
    {
        var document = new BsonDocument
        {
            { "Key", "member-1" },
            { "Name", "No level was ever stored for this member" }
        };

        var member = BsonSerializer.Deserialize<TestMember>(document);

        Assert.Null(member.AccessLevel);
    }

    /// <summary>
    /// The contrast that shows this is fixable: <c>State</c> sits directly above <c>AccessLevel</c> in the
    /// same record and is nullable, so its absence is preserved as "unknown" instead of being invented.
    /// </summary>
    [Fact]
    public void AStoredMemberMissingState_DeserializesAsNull()
    {
        var document = new BsonDocument { { "Key", "member-1" } };

        var member = BsonSerializer.Deserialize<TestMember>(document);

        Assert.Null(member.State);
    }

    /// <summary>
    /// The path a host actually hits. <c>CreateTeamMember</c> is abstract, so an override that forgets to
    /// copy <c>AccessLevel</c> from the invite still compiles — but now produces a member with no level,
    /// which the invite path rejects, rather than an Owner.
    /// </summary>
    [Fact]
    public void AMemberBuiltWithoutChoosingALevel_HasNoLevel()
    {
        var invited = new TestMember { Key = "member-1", Name = "Invited as a Viewer" };

        Assert.Null(invited.AccessLevel);
    }

    /// <summary>
    /// A member with no level is written without the field, like <c>State</c>, so saving it does not invent
    /// a value and the startup check still finds it.
    /// </summary>
    [Fact]
    public void AMemberWithNoLevel_IsStoredWithoutTheField()
    {
        var document = new TestMember { Key = "member-1" }.ToBsonDocument();

        Assert.False(document.Contains("AccessLevel"));
    }

    /// <summary>
    /// Member levels are stored by name, so renumbering the enum would not re-grade a stored member.
    /// </summary>
    [Fact]
    public void AMemberLevel_IsStoredByName()
    {
        var document = new TestMember { Key = "member-1", AccessLevel = AccessLevel.Viewer }.ToBsonDocument();

        Assert.Equal(BsonType.String, document["AccessLevel"].BsonType);
        Assert.Equal("Viewer", document["AccessLevel"].AsString);
    }

    /// <summary>
    /// Consent is too, as of the enum-storage conversion. It previously carried no
    /// <c>BsonRepresentation</c> and so stored the ordinal, which would have made any renumbering of
    /// <see cref="AccessLevel"/> silently re-grade every stored consent decision.
    /// </summary>
    [Fact]
    public void AConsentLevel_IsStoredByName()
    {
        var document = new TestTeam { Key = "team-1", Name = "B", ConsentAccessLevel = AccessLevel.Viewer }
            .ToBsonDocument();

        Assert.Equal(BsonType.String, document["ConsentAccessLevel"].BsonType);
        Assert.Equal("Viewer", document["ConsentAccessLevel"].AsString);
    }

    /// <summary>
    /// The conversion does not strand consent decisions already written as numbers.
    /// </summary>
    [Fact]
    public void AConsentLevelWrittenAsANumber_StillReads()
    {
        var legacy = new BsonDocument
        {
            { "Key", "team-1" },
            { "Name", "B" },
            { "ConsentAccessLevel", (int)AccessLevel.Viewer }
        };

        var team = BsonSerializer.Deserialize<TestTeam>(legacy);

        Assert.Equal(AccessLevel.Viewer, team.ConsentAccessLevel);
    }

    private sealed record TestTeam : TeamEntityBase<TestMember>;
}
