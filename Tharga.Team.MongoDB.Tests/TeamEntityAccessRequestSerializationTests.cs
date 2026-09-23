using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// Access requests and a temporary consent round-trip through the serializer, reach callers through <see cref="ITeam"/>,
/// and leave a team without either byte-identical to one written before they existed.
/// </summary>
public class TeamEntityAccessRequestSerializationTests
{
    public record TestMember : TeamMemberBase;
    public record TestTeamEntity : TeamEntityBase<TestMember>;

    private static readonly DateTime At = new(2026, 9, 14, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void RequestsAndTemporaryConsent_RoundTrip_AndReachITeam()
    {
        var team = new TestTeamEntity
        {
            Key = "team-1",
            Name = "Team",
            Members = [],
            TemporaryConsent = new TemporaryConsentEntity { ExpiresAt = At.AddHours(8), PreviousConsentedRoles = ["Developer"], PreviousConsentAccessLevel = AccessLevel.Viewer },
            AccessRequests =
            [
                new TeamAccessRequestEntity
                {
                    Id = "r1",
                    RequesterKey = "dev",
                    RequesterName = "Dev",
                    AccessLevel = AccessLevel.Administrator,
                    Duration = TimeSpan.FromHours(8),
                    Message = "why",
                    RequestedAt = At,
                    Status = TeamAccessRequestStatus.Approved,
                    DecidedBy = "owner",
                    DecidedAt = At,
                    GrantedUntil = At.AddHours(8)
                }
            ]
        };

        var restored = (ITeam)BsonSerializer.Deserialize<TestTeamEntity>(team.ToBsonDocument());

        var request = Assert.Single(restored.AccessRequests);
        Assert.Equal("r1", request.Id);
        Assert.Equal(AccessLevel.Administrator, request.AccessLevel);
        Assert.Equal(TimeSpan.FromHours(8), request.Duration);
        Assert.Equal(TeamAccessRequestStatus.Approved, request.Status);
        Assert.Equal(At.AddHours(8), request.GrantedUntil);

        Assert.Equal(At.AddHours(8), restored.TemporaryConsent.ExpiresAt);
        Assert.Equal(AccessLevel.Viewer, restored.TemporaryConsent.PreviousConsentAccessLevel);
    }

    [Fact]
    public void ATeamWithoutEither_OmitsBothFields()
    {
        var doc = new TestTeamEntity { Key = "team-1", Name = "Team", Members = [] }.ToBsonDocument();

        Assert.False(doc.Contains(nameof(TeamEntityBase<TestMember>.AccessRequests)));
        Assert.False(doc.Contains(nameof(TeamEntityBase<TestMember>.TemporaryConsent)));
    }
}
