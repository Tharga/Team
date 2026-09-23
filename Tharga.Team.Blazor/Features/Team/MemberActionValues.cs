namespace Tharga.Team.Blazor.Features.Team;

/// <summary>
/// The action values <c>TeamComponent</c> handles itself on a member row. Anything else on that menu came
/// from a host through <c>MemberActionItems</c> and is forwarded to <c>MemberActionInvoked</c>.
/// </summary>
/// <remarks>
/// <b>Named here so the routing rule can be tested.</b> The decision — ours or the host's — would otherwise
/// live inside a component this project cannot render in a test, since it has no bUnit. Keeping the set and
/// the predicate together also means adding a built-in action cannot silently start swallowing a host value
/// that happened to match: the set is the contract, in one place.
/// </remarks>
internal static class MemberActionValues
{
    internal const string CopyInvite = "copy-invite";
    internal const string RemoveMember = "remove-member";
    internal const string MemberAudit = "member-audit";
    internal const string SuspendMember = "suspend-member";
    internal const string RestoreMember = "restore-member";

    /// <summary>Every value the component routes internally.</summary>
    internal static IReadOnlyList<string> All { get; } =
        [CopyInvite, RemoveMember, MemberAudit, SuspendMember, RestoreMember];

    /// <summary>
    /// Whether <paramref name="value"/> is one the component handles itself.
    /// </summary>
    /// <remarks>
    /// A host value that collides with one of these is handled as the built-in, not forwarded — the built-in
    /// behaviour is what a member row's Remove or Suspend has to keep meaning. Hosts pick their own values,
    /// and these five are documented, so a collision is a choice rather than an accident.
    /// </remarks>
    internal static bool IsBuiltIn(string value) => All.Contains(value);
}
