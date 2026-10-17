namespace Tharga.Team;

/// <summary>A team's defaults for automatic notices, used for its members who have not chosen; every member requires the team scope <see cref="NoticeScopes.Manage"/>.</summary>
/// <remarks>
/// <b>Gated</b>: registered as a team service, so a caller without the scope on the team named first is refused before
/// the implementation runs and the refusal is audited. A change is audited by the service itself.
/// </remarks>
public interface IUserNoticeTeamService
{
    /// <summary>The team's default for every notice in the catalogue, in catalogue order.</summary>
    [RequireScope(NoticeScopes.Manage, Audit = AuditMode.None)]
    Task<UserNoticeTeamDefault[]> GetTeamDefaultsAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>Sets whether the team's members receive a notice and how until they choose, audited when it changes.</summary>
    /// <exception cref="ArgumentException">The notice is not in the catalogue, or it does not allow <paramref name="delivery"/>.</exception>
    [RequireScope(NoticeScopes.Manage, Audit = AuditMode.None)]
    Task<UserNoticeTeamDefault> SetTeamDefaultAsync(string teamKey, string noticeKey, bool enabled, UserNoticeDelivery delivery, CancellationToken cancellationToken = default);
}

/// <summary>A team's default for one notice.</summary>
public sealed record UserNoticeTeamDefault
{
    /// <summary>The notice.</summary>
    public required UserNoticeType Notice { get; init; }

    /// <summary>Whether members who have not chosen receive it.</summary>
    public required bool Enabled { get; init; }

    /// <summary>How members who have not chosen receive it.</summary>
    public required UserNoticeDelivery Delivery { get; init; }

    /// <summary>True when the team set something other than the catalogue's default.</summary>
    public bool IsSet { get; init; }
}
