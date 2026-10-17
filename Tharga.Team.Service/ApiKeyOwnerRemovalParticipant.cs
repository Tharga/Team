using Microsoft.Extensions.Options;

namespace Tharga.Team.Service;

/// <summary>
/// Deletes, or disables, a member's private API keys in a team when they stop being a member of it
/// (Tharga/Team#323). Which one is <see cref="ApiKeyOptions.OwnerRemovedAction"/>.
/// </summary>
/// <remarks>
/// <b>Hygiene, not the security fix.</b> Authentication already refuses a private key whose owner is no longer
/// a member. This keeps the keys from being listed after their owner is gone, and from coming back if the same
/// person is invited again — the member key is the user key, so a re-invited member would otherwise own them.
/// <para>
/// Through <see cref="IApiKeyAdministrationService"/> rather than the collection, unlike the purge
/// participant, so it works for a host's own key store and a delete reaches the lifecycle handlers like any
/// other. By the time it runs, the removal was authorized.
/// </para>
/// </remarks>
internal sealed class ApiKeyOwnerRemovalParticipant(IApiKeyAdministrationService apiKeys = null, IOptions<ApiKeyOptions> options = null) : ITeamMemberRemovalParticipant
{
    public string Name => "private API keys";

    public async Task<int> MemberRemovedAsync(string teamKey, string userKey, CancellationToken cancellationToken = default)
    {
        // Registered with the team composition root, which a host without API keys also uses.
        if (apiKeys == null) return 0;

        var action = options?.Value?.OwnerRemovedAction ?? PrivateKeyOwnerRemovedAction.Delete;

        // Collected first: acting while enumerating would change the sequence being read.
        var owned = new List<IApiKey>();
        await foreach (var key in apiKeys.GetKeysAsync(teamKey).WithCancellation(cancellationToken))
        {
            if (string.Equals(key.OwnerMemberKey, userKey, StringComparison.Ordinal)) owned.Add(key);
        }

        var affected = 0;
        foreach (var key in owned)
        {
            if (action == PrivateKeyOwnerRemovedAction.Disable)
            {
                if (key.DisabledAt != null) continue;
                await apiKeys.SetKeyDisabledAsync(teamKey, key.Key, true, "Owner removed from team");
            }
            else
            {
                await apiKeys.DeleteKeyAsync(teamKey, key.Key);
            }

            affected++;
        }

        return affected;
    }
}
