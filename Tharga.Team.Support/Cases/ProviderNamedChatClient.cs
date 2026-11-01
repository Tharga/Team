using Microsoft.Extensions.AI;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// A chat client that reports the provider a team's connection is to, rather than what the client library calls itself.
/// </summary>
/// <remarks>
/// A library names itself in <see cref="ChatClientMetadata"/>, and one library reaches several providers: the Anthropic
/// client serves Neurolito too, and the OpenAI client serves Ollama and llama.cpp. Left alone, an answer from Neurolito
/// was recorded, and shown on the usage page, as Anthropic. The address and model are kept as the client reported them.
/// </remarks>
internal sealed class ProviderNamedChatClient(IChatClient inner, SupportAiConnectionKind kind) : DelegatingChatClient(inner)
{
    public static string NameOf(SupportAiConnectionKind kind) => kind.ToString().ToLowerInvariant();

    public override object GetService(Type serviceType, object serviceKey = null)
    {
        if (serviceKey == null && serviceType == typeof(ChatClientMetadata))
        {
            var reported = base.GetService(serviceType, serviceKey) as ChatClientMetadata;
            return new ChatClientMetadata(NameOf(kind), reported?.ProviderUri, reported?.DefaultModelId);
        }

        return base.GetService(serviceType, serviceKey);
    }
}
