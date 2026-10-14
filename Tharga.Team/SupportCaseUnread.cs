namespace Tharga.Team;

/// <summary>How many entries in one of the caller's own cases they have not read.</summary>
public record SupportCaseUnread
{
    public required string CaseId { get; init; }

    public required int Count { get; init; }
}
