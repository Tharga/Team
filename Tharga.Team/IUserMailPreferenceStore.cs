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

    /// <summary>The stored choices for one category of the users named, by user key; a user who never chose is absent.</summary>
    async Task<IReadOnlyDictionary<string, bool>> GetChoicesForCategoryAsync(string categoryKey, IReadOnlyCollection<string> userKeys, CancellationToken cancellationToken = default)
    {
        var choices = new Dictionary<string, bool>(StringComparer.Ordinal);

        foreach (var userKey in userKeys ?? [])
        {
            if (choices.ContainsKey(userKey)) continue;

            var stored = await GetChoicesAsync(userKey, cancellationToken);
            if (stored != null && stored.TryGetValue(categoryKey, out var receives)) choices[userKey] = receives;
        }

        return choices;
    }
}
