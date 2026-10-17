namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// The create card's Personal choice in <c>ApiKeyView</c> and <c>SystemApiKeyView</c>, from the host's
/// <see cref="ApiKeyOwnership"/> and the view's <c>ShowPrivateKeys</c>.
/// </summary>
internal static class PersonalKeyChoice
{
    /// <summary>
    /// Whether the create card offers the Personal checkbox: only when both kinds are allowed, and the view
    /// lists the caller's personal keys.
    /// </summary>
    /// <remarks>
    /// Without the second condition a key created with the box ticked would vanish from the list the moment
    /// it was created, which reads as the create having failed.
    /// </remarks>
    public static bool ShowCheckbox(ApiKeyOwnership ownership, PrivateKeyScope shown)
        => ownership == ApiKeyOwnership.Both && shown != PrivateKeyScope.None;

    /// <summary>Which personal keys the view asks the service for.</summary>
    /// <remarks>
    /// When every new key is personal the caller's own are always listed: a page left at <c>None</c> would
    /// otherwise never show a single key it creates.
    /// </remarks>
    public static PrivateKeyScope Listed(ApiKeyOwnership ownership, PrivateKeyScope shown)
        => ownership == ApiKeyOwnership.Personal && shown == PrivateKeyScope.None ? PrivateKeyScope.Mine : shown;

    /// <summary>Whether a new key is requested as personal, given whether the checkbox is ticked.</summary>
    /// <remarks>
    /// The service enforces the option as well. Asking for what the option demands means the view never
    /// sends a request the service has to refuse.
    /// </remarks>
    public static bool IsPersonal(ApiKeyOwnership ownership, bool ticked) => ownership switch
    {
        ApiKeyOwnership.Personal => true,
        ApiKeyOwnership.Shared => false,
        _ => ticked,
    };
}
