namespace Tharga.Team;

/// <summary>
/// The member of the product's support staff a case is assigned to.
/// </summary>
/// <remarks>
/// <b>A snapshot, like <see cref="SupportCase.AuthorName"/>.</b> The name and the address are what they were when the case was
/// assigned, so a case still reads sensibly after the person has changed them or left. <see cref="Key"/> is the stable
/// authentication subject, as <see cref="SupportCase.AuthorIdentity"/> and <see cref="SupportCase.ClosedBy"/> are, and may no longer
/// resolve to anybody.
/// </remarks>
public record SupportCaseAssignee
{
    /// <summary>The person's stable authentication subject.</summary>
    public required string Key { get; init; }

    /// <summary>The name to show for the person.</summary>
    public required string Name { get; init; }

    /// <summary>The person's e-mail address, or <c>null</c> when it is not known.</summary>
    public string Email { get; init; }
}
