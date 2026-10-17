using System.ComponentModel;

namespace Tharga.Team;

/// <summary>Keeps each user's mail choices that differ from a category's default; internal and unchecked, so read and change choices through <see cref="IUserMailPreferenceService"/>, never by injecting this.</summary>
/// <remarks><c>Tharga.Team.MongoDB</c> registers one; register another before it to keep choices elsewhere.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public interface IUserMailPreferenceStore
{
    /// <summary>The user's stored choices by category key; empty when they never chose.</summary>
    Task<IReadOnlyDictionary<string, bool>> GetChoicesAsync(string userKey, CancellationToken cancellationToken = default);

    /// <summary>Stores the user's choice for one category, replacing any earlier one.</summary>
    Task SetChoiceAsync(string userKey, string categoryKey, bool receives, CancellationToken cancellationToken = default);

    /// <summary>Removes the user's choice for one category, so its default applies again; nothing happens when there is none.</summary>
    Task ClearChoiceAsync(string userKey, string categoryKey, CancellationToken cancellationToken = default);
}
