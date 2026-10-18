namespace Tharga.Team;

/// <summary>Which automatic notices a user receives and how, read and changed by that user or by staff holding <see cref="SystemUserScopes.Manage"/>.</summary>
/// <remarks>
/// <b>Checked in the service, not by scope attributes</b>, because the rule is "your own, or any with
/// <c>users:manage</c>", which a scope alone cannot say; a refused change is audited.
/// <para>
/// <b>A choice follows the user, not the team.</b> One person has one inbox, so a choice made once applies in every
/// team they are a member of. Until they choose, each team's default applies to that team's notices, then the
/// catalogue's. Only a choice that differs from a default in force for them is stored.
/// </para>
/// </remarks>
public interface IUserNoticePreferenceService
{
    /// <summary>The user's choice for every notice they may receive in any of their teams, in catalogue order.</summary>
    /// <exception cref="UnauthorizedAccessException">The caller is not this user and does not hold <c>users:manage</c>.</exception>
    Task<UserNoticeChoice[]> GetChoicesAsync(string userKey, CancellationToken cancellationToken = default);

    /// <summary>Records whether the user receives a notice and how, audited when it changes, and returns the choice now in force.</summary>
    /// <exception cref="ArgumentException">The notice is not in the catalogue, or it does not allow <paramref name="delivery"/>.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller is not this user and does not hold <c>users:manage</c>.</exception>
    Task<UserNoticeChoice> SetChoiceAsync(string userKey, string noticeKey, bool enabled, UserNoticeDelivery delivery, CancellationToken cancellationToken = default);
}

/// <summary>Whether one user receives one notice and how, and where that comes from.</summary>
public sealed record UserNoticeChoice
{
    /// <summary>The notice.</summary>
    public required UserNoticeType Notice { get; init; }

    /// <summary>The mail category the notice is sent in.</summary>
    public required UserMailCategory Category { get; init; }

    /// <summary>Whether the user receives the notice.</summary>
    public required bool Enabled { get; init; }

    /// <summary>How the user receives it.</summary>
    public required UserNoticeDelivery Delivery { get; init; }

    /// <summary>True when <see cref="Enabled"/> is the user's own choice rather than a default.</summary>
    public bool EnabledChosen { get; init; }

    /// <summary>True when <see cref="Delivery"/> is the user's own choice rather than a default.</summary>
    public bool DeliveryChosen { get; init; }

    /// <summary>True when the user has not chosen and their teams' defaults differ, so the catalogue's default is shown and each team's applies.</summary>
    public bool VariesByTeam { get; init; }

    /// <summary>Whether the user receives the notice's mail category; when false the notice is not sent whatever is chosen here.</summary>
    public bool CategoryReceives { get; init; } = true;
}
