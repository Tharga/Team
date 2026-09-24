namespace Tharga.Team.Blazor.Features.Team;

/// <summary>
/// A consumer-supplied split-button action on a member row: the clicked item's <c>Value</c> and the row's
/// member. Delivered via the <c>MemberActionInvoked</c> callback for items added through
/// <c>MemberActionItems</c>. The <c>TeamComponent</c> counterpart of
/// <see cref="Features.User.TeamRowAction"/> and <see cref="Features.User.UserRowAction"/>.
/// </summary>
/// <remarks>
/// Generic because <c>TeamComponent</c> is: a host that extended the member type gets its own type back,
/// which is the whole point of the hook — the added field is on <typeparamref name="TMember"/>, and a
/// handler receiving <c>ITeamMember</c> would have to cast to reach it.
/// </remarks>
/// <typeparam name="TMember">The host's member type.</typeparam>
/// <param name="Action">The <c>Value</c> of the clicked <c>RadzenSplitButtonItem</c>.</param>
/// <param name="Member">The member row the action was invoked on.</param>
public sealed record MemberRowAction<TMember>(string Action, TMember Member) where TMember : ITeamMember;
