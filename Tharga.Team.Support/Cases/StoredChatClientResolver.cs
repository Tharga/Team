using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Answers each team with the connection it chose: the platform's model, its own provider, or nothing.
/// </summary>
/// <remarks>
/// <b>No record means the platform's model</b>, so turning per-team connections on changes nothing for a team until
/// somebody chooses. Off and a provider the host did not install give no model, so the team is not configured and
/// its cases go to people.
/// <para>
/// A key that can no longer be decrypted, because the key ring was lost, gives no model and is logged; asking the
/// team to enter it again is <see cref="ISupportAiConnectionService"/>'s to say when somebody opens it.
/// </para>
/// </remarks>
internal sealed class StoredChatClientResolver(
    ISupportAiConnectionStore store,
    IDataProtectionProvider dataProtection,
    SupportAiClientCache cache,
    ILogger<StoredChatClientResolver> logger,
    IChatClient platform = null,
    ISupportChatClientFactory factory = null) : ISupportChatClientResolver
{
    private readonly IDataProtector _protector = dataProtection.CreateProtector(SupportAiConnectionService.ProtectionPurpose);

    public async Task<IChatClient> GetAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var stored = teamKey == null ? null : await store.GetAsync(teamKey, cancellationToken);

        switch (stored?.Kind ?? SupportAiConnectionKind.Platform)
        {
            case SupportAiConnectionKind.Platform:
                return platform;
            case SupportAiConnectionKind.Off:
                return null;
        }

        if (factory == null || factory.Providers.All(x => x.Kind != stored.Kind) || stored.ProtectedApiKey == null) return null;

        return cache.GetOrCreate(stored, () =>
        {
            try
            {
                return factory.Create(new SupportAiEndpoint(stored.Kind, stored.BaseAddress, _protector.Unprotect(stored.ProtectedApiKey), stored.Model));
            }
            catch (CryptographicException)
            {
                logger.LogWarning("The stored AI key for team {TeamKey} can no longer be decrypted, so the team has no assistant until the key is entered again.", teamKey);
                return null;
            }
        });
    }
}

/// <summary>
/// One chat client per team, kept until the team's connection changes.
/// </summary>
/// <remarks>
/// <b>Why cache at all:</b> a provider client holds an HTTP connection, and building one for every answer would open
/// a connection for every answer. The version is the record's change time with its protected key, so a new
/// connection, model or key gives a new client on the very next answer.
/// <para>
/// A client that is replaced is not disposed: an answer may still be on its way through it. Replacement happens when
/// somebody changes the connection, which is rare, and the old client is collected like any other object.
/// </para>
/// </remarks>
internal sealed class SupportAiClientCache
{
    private readonly ConcurrentDictionary<string, (string Version, IChatClient Client)> _byTeam = new();

    public IChatClient GetOrCreate(StoredSupportAiConnection connection, Func<IChatClient> create)
    {
        var version = $"{connection.ChangedAt.Ticks}|{connection.Kind}|{connection.BaseAddress}|{connection.Model}|{connection.ProtectedApiKey}";

        if (_byTeam.TryGetValue(connection.TeamKey, out var cached) && cached.Version == version) return cached.Client;

        var client = create();
        if (client != null) _byTeam[connection.TeamKey] = (version, client);
        else _byTeam.TryRemove(connection.TeamKey, out _);

        return client;
    }
}
