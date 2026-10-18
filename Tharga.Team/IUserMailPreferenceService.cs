namespace Tharga.Team;

/// <summary>Which categories of mail a user receives, read and changed by that user or by staff holding <see cref="SystemUserScopes.Manage"/>.</summary>
/// <remarks>
/// <b>Checked in the service, not by scope attributes</b>, because the rule is "your own, or any with
/// <c>users:manage</c>", which a scope alone cannot say. Every member that names a user refuses a caller who is
/// neither that user nor holds the system scope, with <see cref="UnauthorizedAccessException"/>.
/// <para>
/// Choices follow the user, not the team: one user has one set wherever they are a member. Only choices that
/// differ from a category's default are stored, so changing a default changes it for everyone who never chose.
/// </para>
/// </remarks>
public interface IUserMailPreferenceService
{
    /// <summary>Every registered category, built-in ones first and then in the order the host registered them.</summary>
    IReadOnlyList<UserMailCategory> GetCategories();

    /// <summary>Where the host's terms of use are, a site path or an absolute address, which a required category links to as the reason it is always sent; null when the host has none.</summary>
    string GetTermsUrl() => null;

    /// <summary>How often each registered category was sent over the past year, per user who received it, in the order of <see cref="GetCategories"/>; empty when the outbox cannot count.</summary>
    /// <remarks>Statistics across all users that name nobody, so any caller may read them. Computed in one query and kept for a few minutes.</remarks>
    Task<IReadOnlyList<UserMailCategoryFrequency>> GetCategoryFrequenciesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<UserMailCategoryFrequency>>([]);

    /// <summary>The user's effective choice for every registered category, in the order of <see cref="GetCategories"/>.</summary>
    /// <exception cref="UnauthorizedAccessException">The caller is not this user and does not hold <c>users:manage</c>.</exception>
    Task<IReadOnlyList<UserMailChoice>> GetChoicesAsync(string userKey, CancellationToken cancellationToken = default);

    /// <summary>Whether the user receives mail in the category: always for a required one, otherwise their choice or the default.</summary>
    /// <exception cref="ArgumentException">The category is not registered.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller is not this user and does not hold <c>users:manage</c>.</exception>
    Task<bool> ReceivesAsync(string userKey, string categoryKey, CancellationToken cancellationToken = default);

    /// <summary>Records whether the user receives an optional category, audited when the choice changes, and returns the choice now in force.</summary>
    /// <exception cref="ArgumentException">The category is not registered.</exception>
    /// <exception cref="InvalidOperationException">The category is required and <paramref name="receives"/> is false.</exception>
    /// <exception cref="UnauthorizedAccessException">The caller is not this user and does not hold <c>users:manage</c>.</exception>
    Task<UserMailChoice> SetChoiceAsync(string userKey, string categoryKey, bool receives, CancellationToken cancellationToken = default);
}
