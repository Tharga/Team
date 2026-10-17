using Tharga.Team;

namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// How <c>ApiKeyView</c> treats private (owner-scoped) keys, given its <c>ShowPrivateKeys</c> parameter and
/// <see cref="ApiKeyOptions.RequirePrivateKeys"/>.
/// </summary>
/// <remarks>
/// Pure and static so it is unit-testable. The view follows the option without a parameter of its own: one
/// switch, set once in options, is what the server enforces, so the page cannot be configured to disagree.
/// </remarks>
internal static class ApiKeyPrivacy
{
    /// <summary>
    /// Which private keys the view lists. With the option on, at least the caller's own: otherwise a page left
    /// on the default <see cref="PrivateKeyScope.None"/> would hide every key it creates.
    /// </summary>
    public static PrivateKeyScope Listed(PrivateKeyScope showPrivateKeys, bool requirePrivateKeys)
        => requirePrivateKeys && showPrivateKeys == PrivateKeyScope.None ? PrivateKeyScope.Mine : showPrivateKeys;

    /// <summary>Whether the create card shows the "Private" switch.</summary>
    public static bool ShowSwitch(PrivateKeyScope showPrivateKeys, bool requirePrivateKeys)
        => Listed(showPrivateKeys, requirePrivateKeys) != PrivateKeyScope.None;

    /// <summary>Whether the create card asks for a private key. With the option on, always.</summary>
    public static bool CreatesPrivate(bool chosen, bool requirePrivateKeys)
        => chosen || requirePrivateKeys;
}
