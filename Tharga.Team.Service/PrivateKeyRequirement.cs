using Tharga.Team;

namespace Tharga.Team.Service;

/// <summary>
/// The one wording of <see cref="ApiKeyOptions.RequirePrivateKeys"/>, shared by every place a team key is
/// created and by the startup check, so the refusal reads the same whichever path a caller took.
/// </summary>
/// <remarks>
/// <b>Refused, never coerced.</b> Turning a request for a team-wide key into a private one would bind the key
/// to whoever happened to call, which is exactly the "who is behind this key?" question the option exists to
/// answer. A refusal names the option instead.
/// </remarks>
internal static class PrivateKeyRequirement
{
    internal const string MissingOwnerMessage =
        "Every new team API key must be private (ApiKeyOptions.RequirePrivateKeys is on), so this key needs an " +
        "owner. Create it owner-scoped: IApiKeyManagementService.CreateKeyAsync(..., ownerScoped: true), or " +
        "pass ownerMemberKey to IApiKeyAdministrationService.CreateKeyAsync.";

    internal const string SimpleModeMessage =
        "ApiKeyOptions.RequirePrivateKeys requires ApiKeyOptions.AdvancedMode. Simple mode (AdvancedMode = false) " +
        "auto-creates team keys that have no owner, and offers no way to create a private one. Turn on " +
        "AdvancedMode, or turn off RequirePrivateKeys.";

    /// <summary>Throws when the option is on and the key would have no owner.</summary>
    internal static void Ensure(ApiKeyOptions options, bool hasOwner)
    {
        if (options?.RequirePrivateKeys == true && !hasOwner)
            throw new InvalidOperationException(MissingOwnerMessage);
    }
}
