using Tharga.Team.Blazor.Features.Team;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// How a click on a member row's action menu is routed: to the component for its own five actions, to the
/// host for anything it added through <c>MemberActionItems</c> (Tharga/Team#294).
/// </summary>
/// <remarks>
/// The routing itself lives in a component this project cannot render — there is no bUnit — so the decision
/// was extracted to <see cref="MemberActionValues"/> and is pinned here. What cannot be asserted this way is
/// the wiring; what can be is the rule that decides ownership, which is where a mistake would be silent.
/// </remarks>
public class MemberActionRoutingTests
{
    [Theory]
    [InlineData(MemberActionValues.CopyInvite)]
    [InlineData(MemberActionValues.RemoveMember)]
    [InlineData(MemberActionValues.MemberAudit)]
    [InlineData(MemberActionValues.SuspendMember)]
    [InlineData(MemberActionValues.RestoreMember)]
    public void EveryBuiltInValue_IsRecognisedAsTheComponentsOwn(string value)
        => Assert.True(MemberActionValues.IsBuiltIn(value));

    /// <summary>
    /// A host's own value is not the component's, and so is forwarded rather than swallowed.
    /// </summary>
    /// <remarks>
    /// This is the whole contract of the hook: the component cannot know what a host will add, so anything
    /// unrecognised must reach <c>MemberActionInvoked</c>. Before #294 there was no such path and an
    /// unmatched value fell off the end of a switch, silently.
    /// </remarks>
    [Theory]
    [InlineData("set-project")]
    [InlineData("copy-invite-link")]     // close to a built-in, deliberately: prefixes must not match
    [InlineData("REMOVE-MEMBER")]        // casing differs, so it is the host's
    [InlineData("")]
    public void AHostValue_IsNotTreatedAsBuiltIn(string value)
        => Assert.False(MemberActionValues.IsBuiltIn(value));

    [Fact]
    public void NullValue_IsNotBuiltIn()
        => Assert.False(MemberActionValues.IsBuiltIn(null));

    /// <summary>The documented set is exactly what the component routes — no more, no fewer.</summary>
    /// <remarks>
    /// Pinned because the set is a published contract: it is what a host reads to choose a value that will
    /// not collide. Adding a built-in action without updating the docs would start swallowing a host value
    /// that used to work, and the host would see their action stop firing with nothing logged.
    /// </remarks>
    [Fact]
    public void TheBuiltInSet_IsTheFiveDocumentedValues()
        => Assert.Equal(
            ["copy-invite", "remove-member", "member-audit", "suspend-member", "restore-member"],
            MemberActionValues.All);

    /// <summary>A row action carries the host's own member type, not the interface.</summary>
    /// <remarks>
    /// The point of the generic: the field a host added lives on their type, and a handler given
    /// <c>ITeamMember</c> would have to cast to reach the very thing the hook exists for.
    /// </remarks>
    [Fact]
    public void RowAction_CarriesTheHostsMemberType()
    {
        var member = new TestMember { Key = "m1", Name = "Ada" };

        var action = new MemberRowAction<TestMember>("set-project", member);

        Assert.Equal("set-project", action.Action);
        Assert.Equal("Ada", action.Member.Name);
    }

    private sealed record TestMember : ITeamMember
    {
        public string Key { get; init; }
        public string Name { get; init; }
        public Invitation Invitation { get; init; }
        public DateTime? LastSeen { get; init; }
        public MembershipState? State { get; init; }
        public AccessLevel AccessLevel { get; init; }
        public string[] TenantRoles { get; init; }
        public string[] ScopeOverrides { get; init; }
    }
}
