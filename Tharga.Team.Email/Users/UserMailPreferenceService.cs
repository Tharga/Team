using Tharga.Team.Service;

namespace Tharga.Team.Email.Users;

/// <summary>The checked surface over mail choices: a user reads and changes their own, and a caller holding <see cref="SystemUserScopes.Manage"/> anyone's.</summary>
/// <remarks>A refused change is audited as a failure, because an attempt to change somebody else's mail is worth knowing about.</remarks>
internal sealed class UserMailPreferenceService(
    UserMailPreferences preferences,
    UserMailCategoryRegistry categories,
    TeamAuthorizer authorizer,
    IUserService userService,
    UserMailOptions options = null) : IUserMailPreferenceService
{
    public IReadOnlyList<UserMailCategory> GetCategories() => categories.All;

    public string GetTermsUrl() => string.IsNullOrWhiteSpace(options?.TermsUrl) ? null : options.TermsUrl.Trim();

    public async Task<IReadOnlyList<UserMailChoice>> GetChoicesAsync(string userKey, CancellationToken cancellationToken = default)
    {
        await RequireSelfOrManageAsync(userKey, nameof(GetChoicesAsync));

        return await preferences.GetChoicesAsync(userKey, cancellationToken);
    }

    public async Task<bool> ReceivesAsync(string userKey, string categoryKey, CancellationToken cancellationToken = default)
    {
        var category = categories.Get(categoryKey, nameof(categoryKey));
        await RequireSelfOrManageAsync(userKey, nameof(ReceivesAsync));

        return await preferences.ReceivesAsync(userKey, category, cancellationToken);
    }

    public async Task<UserMailChoice> SetChoiceAsync(string userKey, string categoryKey, bool receives, CancellationToken cancellationToken = default)
    {
        var category = categories.Get(categoryKey, nameof(categoryKey));

        try
        {
            await RequireSelfOrManageAsync(userKey, nameof(SetChoiceAsync));
        }
        catch (UnauthorizedAccessException e)
        {
            preferences.AuditRefusal(UserMailAudit.ChoiceAction, userKey, categoryKey, receives, e.Message);
            throw;
        }

        return await preferences.SetAsync(userKey, category, receives, UserMailAudit.ChoiceAction, cancellationToken);
    }

    private async Task RequireSelfOrManageAsync(string userKey, string operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userKey);

        var current = await userService.GetCurrentUserAsync();
        if (current != null && string.Equals(current.Key, userKey, StringComparison.Ordinal)) return;

        if (await authorizer.HasSystemScopeAsync(SystemUserScopes.Manage)) return;

        throw new UnauthorizedAccessException($"{operation} on another user's mail choices requires the '{SystemUserScopes.Manage}' system scope.");
    }
}
