namespace Tharga.Team;

public record InviteUserModel
{
    public string Name { get; set; }
    public string Email { get; set; }

    /// <summary>
    /// The level the invitee joins at. <b>Required</b>: an invitation with none is rejected by
    /// <c>ITeamService.AddMemberAsync</c>. Nullable so that forgetting it is an error rather than an Owner, the
    /// enum's zero value — which is what it silently was before 4.0.
    /// </summary>
    public AccessLevel? AccessLevel { get; set; }
}