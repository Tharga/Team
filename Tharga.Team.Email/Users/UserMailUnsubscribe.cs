namespace Tharga.Team.Email.Users;

/// <summary>Turns off one optional category for one user on the authority of an unsubscribe link, with no sign-in.</summary>
/// <remarks>
/// <b>The token is the check.</b> It names the user and the category and cannot be altered, so it authorizes exactly
/// that one change and nothing else; a required category is never turned off this way. The change is audited as
/// an unsubscribe.
/// </remarks>
internal sealed class UserMailUnsubscribe(UserMailUnsubscribeToken tokens, UserMailCategoryRegistry categories, UserMailPreferences preferences)
{
    /// <summary>The category the link would turn off, or null when the link is not valid.</summary>
    public UserMailCategory Read(string token) => Resolve(token, out _);

    /// <summary>Turns off the category the link names and returns it, or returns null when the link is not valid; turning off a category already off changes nothing.</summary>
    public async Task<UserMailCategory> UnsubscribeAsync(string token, CancellationToken cancellationToken)
    {
        var category = Resolve(token, out var userKey);
        if (category == null) return null;

        await preferences.SetAsync(userKey, category, false, UserMailAudit.UnsubscribeAction, cancellationToken);

        return category;
    }

    private UserMailCategory Resolve(string token, out string userKey)
    {
        if (!tokens.TryRead(token, out userKey, out var categoryKey)) return null;

        var category = categories.Find(categoryKey);

        return category is { Required: false } ? category : null;
    }
}
