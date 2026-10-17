namespace Tharga.Team;

/// <summary>
/// Which kinds of API key people can create: shared keys, personal keys, or both. Set per key kind with
/// <see cref="ApiKeyOptions.TeamKeys"/> and <see cref="ApiKeyOptions.SystemKeys"/>.
/// </summary>
/// <remarks>
/// A personal key belongs to the person who created it: only they, or a caller holding the
/// <see cref="SystemApiKeyScopes.Oversee"/> system scope, see and manage it. In code a personal key is an
/// owner-scoped ("private") key, see <see cref="IApiKey.OwnerMemberKey"/>.
/// <para>
/// The option governs creation only. Keys that already exist are not changed when it changes, and stay
/// listed and manageable.
/// </para>
/// </remarks>
public enum ApiKeyOwnership
{
    /// <summary>Both kinds. The create card offers a Personal checkbox. Default.</summary>
    Both,

    /// <summary>
    /// Shared keys only. The create card offers no checkbox, and a request for a personal key is refused,
    /// because silently making it shared would expose a key its creator meant to keep to themselves.
    /// </summary>
    Shared,

    /// <summary>
    /// Personal keys only. The create card offers no checkbox, and every key a person creates is personal,
    /// whatever the caller asked for.
    /// </summary>
    Personal,
}
