using Microsoft.Extensions.AI;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Turns a team's provider connection into a chat client, and lists the models a provider offers.
/// </summary>
/// <remarks>
/// <b>The one place a provider's protocol is known.</b> <c>Tharga.Team.Support.Anthropic</c> implements it for the
/// Anthropic Messages API, which Neurolito speaks too, so this package names no vendor and takes no vendor SDK. A
/// host can implement it for a provider the toolkit does not cover.
/// </remarks>
public interface ISupportChatClientFactory
{
    /// <summary>The provider kinds this factory reaches, each with the address it starts from.</summary>
    IReadOnlyList<SupportAiProviderDefault> Providers { get; }

    /// <summary>A chat client answering with the endpoint's model, through the endpoint's key.</summary>
    IChatClient Create(SupportAiEndpoint endpoint);

    /// <summary>The models the provider offers for the key, or why they could not be listed.</summary>
    /// <remarks>A refused key, an unreachable address and a timeout are problems for the person setting it up, so they come back as <see cref="SupportAiModelList.Problem"/>, never as an exception.</remarks>
    Task<SupportAiModelList> ListModelsAsync(SupportAiEndpoint endpoint, CancellationToken cancellationToken = default);
}

/// <summary>
/// Where and how to reach a provider: the kind, the address, the key in clear, and the model.
/// </summary>
/// <remarks>
/// Built inside the service and the resolver from a decrypted key and handed to the factory; it never crosses a
/// boundary. Its text hides the key, like <see cref="SupportAiProviderRequest"/>.
/// </remarks>
public sealed record SupportAiEndpoint(SupportAiConnectionKind Kind, string BaseAddress, string ApiKey, string Model)
{
    /// <inheritdoc />
    public override string ToString()
        => $"{nameof(SupportAiEndpoint)} {{ Kind = {Kind}, BaseAddress = {BaseAddress}, Model = {Model}, ApiKey = {(ApiKey == null ? "null" : "(hidden)")} }}";
}
