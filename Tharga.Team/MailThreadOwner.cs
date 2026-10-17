namespace Tharga.Team;

/// <summary>
/// What a mail belongs to, in the host's terms — a support case, a case in a register — named by a type and an
/// id the toolkit does not interpret.
/// </summary>
public record MailThreadOwner
{
    public required string Type { get; init; }

    public required string Id { get; init; }
}
