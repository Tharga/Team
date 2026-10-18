using System.ComponentModel;

namespace Tharga.Team;

/// <summary>Keeps users' notice choices and teams' notice defaults that differ from a default; internal and unchecked, so change them through <see cref="IUserNoticePreferenceService"/> and <see cref="IUserNoticeTeamService"/>, never by injecting this.</summary>
/// <remarks><c>Tharga.Team.MongoDB</c> registers one; register another before it to keep them elsewhere.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IUserNoticePreferenceStore
{
    /// <summary>The user's stored choices by notice key; empty when they never chose.</summary>
    Task<IReadOnlyDictionary<string, UserNoticeSetting>> GetChoicesAsync(string userKey, CancellationToken cancellationToken = default);

    /// <summary>The stored choices for one notice of the users named, by user key; a user who never chose is absent.</summary>
    Task<IReadOnlyDictionary<string, UserNoticeSetting>> GetChoicesForNoticeAsync(string noticeKey, IReadOnlyCollection<string> userKeys, CancellationToken cancellationToken = default);

    /// <summary>Stores the user's choice for one notice, replacing any earlier one, or removes it when the setting is empty.</summary>
    Task SetChoiceAsync(string userKey, string noticeKey, UserNoticeSetting setting, CancellationToken cancellationToken = default);

    /// <summary>The team's stored defaults by notice key; empty when it never set one.</summary>
    Task<IReadOnlyDictionary<string, UserNoticeSetting>> GetTeamDefaultsAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>Stores the team's default for one notice, replacing any earlier one, or removes it when the setting is empty.</summary>
    Task SetTeamDefaultAsync(string teamKey, string noticeKey, UserNoticeSetting setting, CancellationToken cancellationToken = default);

    /// <summary>Removes every default the team set, returning how many there were.</summary>
    Task<int> RemoveTeamDefaultsAsync(string teamKey, CancellationToken cancellationToken = default);
}

/// <summary>What a user or team set for one notice; a part left null follows the next default.</summary>
public sealed record UserNoticeSetting
{
    /// <summary>Whether the notice is received, or null to follow the default.</summary>
    public bool? Enabled { get; init; }

    /// <summary>How the notice is delivered, or null to follow the default.</summary>
    public UserNoticeDelivery? Delivery { get; init; }

    /// <summary>True when nothing is set, so nothing needs storing.</summary>
    public bool IsEmpty => Enabled == null && Delivery == null;
}
