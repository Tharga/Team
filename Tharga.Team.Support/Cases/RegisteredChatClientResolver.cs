using Microsoft.Extensions.AI;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Hands every team the one <see cref="IChatClient"/> the host registered, which is how the assistant worked
/// before a resolver existed.
/// </summary>
internal sealed class RegisteredChatClientResolver(IChatClient chat) : ISupportChatClientResolver
{
    public Task<IChatClient> GetAsync(string teamKey, CancellationToken cancellationToken = default) => Task.FromResult(chat);
}
