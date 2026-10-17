using Tharga.Team;
using Tharga.Team.Blazor.Features.Team;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The Members column: how many people are in a team, and how many are only invited so far.
/// </summary>
/// <remarks>
/// A single number would count invitations as members, which overstates a team that has just been set
/// up — the most common moment for someone to be reading this list.
/// </remarks>
public class TeamMemberCountsTests
{
    private sealed record FakeMember(MembershipState? State, DateTime? SuspendedAt = null) : ITeamMember
    {
        public string Key => Guid.NewGuid().ToString();
        public string Name => null;
        public Invitation Invitation => null;
        public DateTime? LastSeen => null;
        public AccessLevel? AccessLevel => Tharga.Team.AccessLevel.User;
        public string[] TenantRoles => [];
        public string[] ScopeOverrides => [];
    }

    private static FakeMember Member(DateTime? suspended = null) => new(MembershipState.Member, suspended);
    private static FakeMember Invited() => new(MembershipState.Invited);

    [Fact]
    public void AnInvitationIsNotYetAMember()
    {
        var count = TeamMemberCounts.Of([Member(), Member(), Invited()]);

        Assert.Equal(2, count.Members);
        Assert.Equal(1, count.Invited);
        Assert.Equal("2 (+1)", count.Text);
    }

    [Fact]
    public void WithNoInvitations_TheCountIsJustANumber()
    {
        Assert.Equal("3", TeamMemberCounts.Of([Member(), Member(), Member()]).Text);
    }

    [Fact]
    public void SuspendedMembersAreStillMembers_AndAreCountedSeparately()
    {
        var count = TeamMemberCounts.Of([Member(), Member(DateTime.UtcNow)]);

        Assert.Equal(2, count.Members);
        Assert.Equal(1, count.Suspended);
        Assert.Contains("1 suspended", count.Title);
    }

    [Fact]
    public void TheTooltipSpellsOutWhatTheParentheticalMeans()
    {
        var count = TeamMemberCounts.Of([Member(), Invited()]);

        Assert.Equal("1 member, 1 invited", count.Title);
    }

    [Fact]
    public void OneMemberIsNotPluralised()
    {
        Assert.Equal("1 member", TeamMemberCounts.Of([Member()]).Title);
    }

    [Theory]
    [InlineData(0)]
    public void AnEmptyTeamCountsAsZero(int expected)
    {
        Assert.Equal(expected, TeamMemberCounts.Of([]).Members);
        Assert.Equal("0", TeamMemberCounts.Of([]).Text);
    }

    /// <summary>
    /// A roster can be null — <c>ITeam&lt;TMember&gt;.Members</c> is an array a host populates, and a
    /// column that throws would take the whole grid down rather than showing one empty cell.
    /// </summary>
    [Fact]
    public void ANullRosterIsNotACrash()
    {
        var count = TeamMemberCounts.Of(null);

        Assert.Equal(0, count.Members);
        Assert.Equal("0", count.Text);
        Assert.Equal("0 members", count.Title);
    }

    [Fact]
    public void ANullMemberInTheRosterIsSkipped()
    {
        Assert.Equal(1, TeamMemberCounts.Of([Member(), null]).Members);
    }

    /// <summary>
    /// A member whose state is absent is treated as not-yet-accepted. `State` is nullable, and counting
    /// an unknown as a full member is the direction that overstates.
    /// </summary>
    [Fact]
    public void AnUnknownStateIsNotCountedAsAMember()
    {
        var count = TeamMemberCounts.Of([new FakeMember(null)]);

        Assert.Equal(0, count.Members);
        Assert.Equal(1, count.Invited);
    }
}
