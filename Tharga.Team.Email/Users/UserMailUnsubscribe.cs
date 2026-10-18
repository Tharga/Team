namespace Tharga.Team.Email.Users;

/// <summary>Turns off one optional category for one user on the authority of an unsubscribe link, with no sign-in.</summary>
/// <remarks>
/// <b>The token is the check.</b> It names the user and the category and cannot be altered, so it authorizes exactly
/// that one change and nothing else; a required category is never turned off this way. The change is audited as
/// an unsubscribe.
/// </remarks>
internal sealed class UserMailUnsubscribe(UserMailUnsubscribeToken tokens, UserMailCategoryRegistry categories, UserMailPreferences preferences)
{
    /// <summary>The user and the category the link would turn off, or null when the link is not valid.</summary>
    public UserMailUnsubscribeLink Read(string token) => Resolve(token);

    /// <summary>Turns off the category the link names and returns the link, or returns null when the link is not valid; turning off a category already off changes nothing.</summary>
    public async Task<UserMailUnsubscribeLink> UnsubscribeAsync(string token, CancellationToken cancellationToken)
    {
        var link = Resolve(token);
        if (link == null) return null;

        await preferences.SetAsync(link.UserKey, link.Category, false, UserMailAudit.UnsubscribeAction, cancellationToken);

        return link;
    }

    private UserMailUnsubscribeLink Resolve(string token)
    {
        if (!tokens.TryRead(token, out var userKey, out var categoryKey)) return null;

        var category = categories.Find(categoryKey);

        return category is { Required: false } ? new UserMailUnsubscribeLink(userKey, category) : null;
    }
}

/// <summary>What an unsubscribe link names: one user and one optional category.</summary>
internal sealed record UserMailUnsubscribeLink(string UserKey, UserMailCategory Category);
