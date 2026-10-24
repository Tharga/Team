using Microsoft.Extensions.AI;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// The chat model the support assistant answers a team with.
/// </summary>
/// <remarks>
/// <b>Register one to give teams different models</b> — a team's own provider, a shared platform account, or
/// none. The default hands every team the <see cref="IChatClient"/> the host registered, so a host with one
/// model for everybody registers nothing here. Instructions, the transcript limit, the timeout and the tools
/// stay with the assistant; only where the model comes from is the host's to decide.
/// <para>
/// Asked on every answer and every readiness check rather than once, so a team that sets up or removes its
/// model is answered accordingly from its next message.
/// </para>
/// </remarks>
public interface ISupportChatClientResolver
{
    /// <summary>
    /// The chat client to answer this team with, or null when the assistant has nothing to answer it with.
    /// </summary>
    /// <remarks>
    /// Null makes the team <see cref="SupportAssistantReadiness.NotConfigured"/>: it is not offered the
    /// assistant, and a case already with the assistant is declined and stays open for a person.
    /// </remarks>
    Task<IChatClient> GetAsync(string teamKey, CancellationToken cancellationToken = default);
}
