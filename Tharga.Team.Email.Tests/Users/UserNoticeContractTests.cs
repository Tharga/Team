using System.Text.Json;
using Tharga.Team.Email.Users;

namespace Tharga.Team.Email.Tests.Users;

/// <summary>What the notice services return crosses a wire: the catalogue entry serializes without its host-side rule, and a raised occurrence can be followed by its id.</summary>
public class UserNoticeContractTests
{
    private readonly UserNoticeFixture _fixture = new();

    [Fact]
    public void AChoice_SerializesWithoutTheAudience_AndReadsBack()
    {
        var choice = new UserNoticeChoice
        {
            Notice = UserNoticeFixture.Assigned with { Audience = UserNoticeAudience.ResolvedBy((_, _, _) => Task.FromResult<IEnumerable<string>>([])) },
            Category = new UserMailCategory { Key = UserNoticeFixture.Work, Name = "Work" },
            Enabled = true,
            Delivery = UserNoticeDelivery.Digest,
            DeliveryChosen = true
        };

        var json = JsonSerializer.Serialize(choice);
        var read = JsonSerializer.Deserialize<UserNoticeChoice>(json);

        Assert.DoesNotContain("Audience", json);
        Assert.DoesNotContain("Resolver", json);
        Assert.Equal(UserNoticeFixture.Assigned.Key, read.Notice.Key);
        Assert.Equal(UserNoticeFixture.Assigned.NameKey, read.Notice.NameKey);
        Assert.Equal([UserNoticeDelivery.AtOnce, UserNoticeDelivery.Digest], read.Notice.Deliveries);
        Assert.Equal(UserNoticeDelivery.Digest, read.Delivery);
        Assert.Null(read.Notice.Audience);
    }

    [Fact]
    public void ATeamDefault_SerializesWithoutTheAudience()
    {
        var json = JsonSerializer.Serialize(new UserNoticeTeamDefault { Notice = UserNoticeFixture.Opened, Enabled = false, Delivery = UserNoticeDelivery.AtOnce, IsSet = true });

        Assert.DoesNotContain("Audience", json);
        Assert.False(JsonSerializer.Deserialize<UserNoticeTeamDefault>(json).Enabled);
    }

    [Fact]
    public async Task ARaisedOccurrence_CanBeFollowedByItsId()
    {
        var id = await _fixture.RaiseAsync(UserNoticeFixture.Joined, "new");

        Assert.Equal(UserNoticeStatus.Pending, (await _fixture.Raiser.GetAsync(id)).Status);

        await _fixture.RunAsync();

        var record = await _fixture.Raiser.GetAsync(id);
        Assert.Equal(UserNoticeStatus.Distributed, record.Status);
        Assert.Equal(2, record.Recipients);
        Assert.Null(await _fixture.Raiser.GetAsync(" "));
    }

    [Fact]
    public async Task MembersHoldingTheSameInATeam_ShareOneScopeLookup()
    {
        var roles = Substitute.For<ITenantRoleService>();
        roles.GetEffectiveScopesAsync(Arg.Any<string>(), Arg.Any<AccessLevel>(), Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>())
            .Returns(Task.FromResult<IReadOnlyList<string>>([UserNoticeFixture.CaseRead]));
        var eligibility = new UserNoticeEligibility(roles);

        foreach (var userKey in new[] { "u-1", "u-2", "u-3" })
            Assert.True(await eligibility.MayReceiveAsync(UserNoticeFixture.Opened, new UserMessageMember { TeamKey = "t", UserKey = userKey, AccessLevel = AccessLevel.User }));
        await eligibility.MayReceiveAsync(UserNoticeFixture.Opened, new UserMessageMember { TeamKey = "t", UserKey = "u-4", AccessLevel = AccessLevel.Viewer });

        await roles.Received(2).GetEffectiveScopesAsync(Arg.Any<string>(), Arg.Any<AccessLevel>(), Arg.Any<IEnumerable<string>>(), Arg.Any<IEnumerable<string>>());
    }
}
