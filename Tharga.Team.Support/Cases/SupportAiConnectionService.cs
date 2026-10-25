using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Tharga.Team.Service;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Each team's AI connection: what the assistant answers the team with, and the team's key for a provider.
/// </summary>
/// <remarks>
/// <b>The key is protected before the store sees it</b>, under a Data Protection purpose of its own, so a value
/// protected for anything else cannot be read back as a key and a database dump holds only ciphertext. It is
/// unprotected in one place, <see cref="EndpointAsync"/>, to reach the provider, and goes nowhere else.
/// <para>
/// <b>A stored key is only ever sent where it was entered for.</b> A request may leave the key out to keep the
/// stored one, but only while the kind and the address are unchanged; otherwise anyone allowed to change the
/// address could have the team's key sent to a server of their choosing.
/// </para>
/// <para>
/// Authorization is the decorator's, and auditing the decorator around it, as for support cases.
/// </para>
/// </remarks>
internal sealed class SupportAiConnectionService(
    ISupportAiConnectionStore store,
    IDataProtectionProvider dataProtection,
    TeamAuthorizer authorizer,
    TimeProvider timeProvider,
    bool platformModelAvailable,
    ISupportChatClientFactory factory = null,
    string platformModelName = null) : ISupportAiConnectionService
{
    internal const string ProtectionPurpose = "Tharga.Team.Support.AiConnection.ApiKey.v1";
    internal const string TestPrompt = "Reply with one short sentence saying that you can answer.";
    internal const int TestMaxOutputTokens = 64;
    internal const string Hidden = "(hidden)";
    internal static readonly TimeSpan TestTimeout = TimeSpan.FromSeconds(60);

    private readonly IDataProtector _protector = dataProtection.CreateProtector(ProtectionPurpose);

    public Task<SupportAiConnectionChoices> GetChoicesAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var providers = factory?.Providers ?? [];

        SupportAiConnectionKind[] kinds =
        [
            .. platformModelAvailable ? [SupportAiConnectionKind.Platform] : Array.Empty<SupportAiConnectionKind>(),
            .. providers.Select(x => x.Kind),
            SupportAiConnectionKind.Off
        ];

        return Task.FromResult(new SupportAiConnectionChoices
        {
            Kinds = kinds,
            ProviderDefaults = [.. providers],
            PlatformModel = platformModelAvailable ? platformModelName : null
        });
    }

    /// <summary>
    /// What the platform's model is, from the <see cref="ChatClientMetadata"/> its client reports: the provider and the
    /// model, such as "ollama, llama3". Null when the client says neither, or there is none.
    /// </summary>
    /// <remarks>A client wrapped for function calls passes the question on to the one inside, so the usual registration is described.</remarks>
    internal static string DescribePlatformModel(IChatClient client)
    {
        var metadata = client?.GetService<ChatClientMetadata>();
        string[] parts = [.. new[] { metadata?.ProviderName, metadata?.DefaultModelId }.Where(x => !string.IsNullOrWhiteSpace(x))];
        return parts.Length == 0 ? null : string.Join(", ", parts);
    }

    public async Task<SupportAiConnection> GetConnectionAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var stored = await store.GetAsync(teamKey, cancellationToken);
        if (stored == null) return new SupportAiConnection();

        return new SupportAiConnection
        {
            Kind = stored.Kind,
            BaseAddress = stored.BaseAddress,
            Model = stored.Model,
            HasApiKey = stored.ProtectedApiKey != null,
            ChangedAt = stored.ChangedAt,
            ChangedBy = stored.ChangedByName
        };
    }

    public async Task UsePlatformModelAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        if (!platformModelAvailable) throw new InvalidOperationException("This site has no platform model to answer with.");

        await SaveAsync(teamKey, SupportAiConnectionKind.Platform, null, null, null, cancellationToken);
    }

    public Task TurnOffAsync(string teamKey, CancellationToken cancellationToken = default)
        => SaveAsync(teamKey, SupportAiConnectionKind.Off, null, null, null, cancellationToken);

    public async Task<SupportAiConnectionResult> ConnectAsync(string teamKey, SupportAiProviderRequest request, CancellationToken cancellationToken = default)
    {
        var (endpoint, problem) = await EndpointAsync(teamKey, request, requireModel: true, cancellationToken);
        if (problem != null) return SupportAiConnectionResult.Failure(problem);

        // Listing the models proves the address and the key without spending a token, and shows the model is
        // one the key may use. Nothing is saved unless it does.
        var models = await factory.ListModelsAsync(endpoint, cancellationToken);
        if (models.Problem != null) return SupportAiConnectionResult.Failure(Hide(models.Problem, endpoint.ApiKey));
        if (!models.Models.Any(x => string.Equals(x.Id, endpoint.Model, StringComparison.Ordinal)))
            return SupportAiConnectionResult.Failure($"The provider does not offer '{endpoint.Model}' for this key.");

        await SaveAsync(teamKey, endpoint.Kind, endpoint.BaseAddress, endpoint.Model, _protector.Protect(endpoint.ApiKey), cancellationToken);

        return SupportAiConnectionResult.Success();
    }

    public async Task<SupportAiModelList> ListModelsAsync(string teamKey, SupportAiProviderRequest request, CancellationToken cancellationToken = default)
    {
        var (endpoint, problem) = await EndpointAsync(teamKey, request, requireModel: false, cancellationToken);
        if (problem != null) return new SupportAiModelList { Problem = problem };

        var models = await factory.ListModelsAsync(endpoint, cancellationToken);
        return models.Problem == null ? models : models with { Problem = Hide(models.Problem, endpoint.ApiKey) };
    }

    public async Task<SupportAiConnectionResult> TestAsync(string teamKey, SupportAiProviderRequest request, CancellationToken cancellationToken = default)
    {
        var (endpoint, problem) = await EndpointAsync(teamKey, request, requireModel: true, cancellationToken);
        if (problem != null) return SupportAiConnectionResult.Failure(problem);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TestTimeout);

        try
        {
            using var chat = factory.Create(endpoint);
            var response = await chat.GetResponseAsync(TestPrompt, new ChatOptions { MaxOutputTokens = TestMaxOutputTokens }, deadline.Token);
            var reply = response.Text?.Trim();

            return string.IsNullOrWhiteSpace(reply)
                ? SupportAiConnectionResult.Failure("The model answered with nothing.")
                : SupportAiConnectionResult.Success(reply);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return SupportAiConnectionResult.Failure("The model did not answer in time.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return SupportAiConnectionResult.Failure($"The model could not answer: {Hide(ex.Message, endpoint.ApiKey)}");
        }
    }

    /// <summary>The provider, address, key and model a request names, or why it cannot be used.</summary>
    private async Task<(SupportAiEndpoint Endpoint, string Problem)> EndpointAsync(string teamKey, SupportAiProviderRequest request, bool requireModel, CancellationToken cancellationToken)
    {
        if (request == null) return (null, "Choose a provider.");

        var provider = factory?.Providers.FirstOrDefault(x => x.Kind == request.Kind);
        if (provider == null) return (null, $"{request.Kind} is not offered here.");

        var baseAddress = Normalize(string.IsNullOrWhiteSpace(request.BaseAddress) ? provider.BaseAddress : request.BaseAddress);
        if (!IsAllowedAddress(baseAddress)) return (null, "The address must be https, or http on this machine.");

        var model = request.Model?.Trim();
        if (requireModel && string.IsNullOrEmpty(model)) return (null, "Choose a model.");

        var apiKey = request.ApiKey?.Trim();
        if (string.IsNullOrEmpty(apiKey))
        {
            var stored = await store.GetAsync(teamKey, cancellationToken);
            if (stored?.ProtectedApiKey == null || stored.Kind != request.Kind || !string.Equals(Normalize(stored.BaseAddress), baseAddress, StringComparison.OrdinalIgnoreCase))
                return (null, "Enter the key.");

            try
            {
                apiKey = _protector.Unprotect(stored.ProtectedApiKey);
            }
            catch (CryptographicException)
            {
                // The key ring that protected it is gone: not shared between instances, or reset. Asking for the key
                // again is the only way back, and saying so beats failing every answer quietly.
                return (null, "The stored key can no longer be read. Enter it again.");
            }
        }

        return (new SupportAiEndpoint(request.Kind, baseAddress, apiKey, model), null);
    }

    private async Task SaveAsync(string teamKey, SupportAiConnectionKind kind, string baseAddress, string model, string protectedApiKey, CancellationToken cancellationToken)
        => await store.SetAsync(new StoredSupportAiConnection
        {
            TeamKey = teamKey,
            Kind = kind,
            BaseAddress = baseAddress,
            Model = model,
            ProtectedApiKey = protectedApiKey,
            ChangedAt = timeProvider.GetUtcNow().UtcDateTime,
            ChangedByIdentity = await authorizer.GetSubjectAsync(),
            ChangedByName = await authorizer.GetDisplayNameAsync()
        }, cancellationToken);

    internal static string Normalize(string address) => address?.Trim().TrimEnd('/');

    /// <summary>https anywhere, http only on this machine, and never credentials in the address itself.</summary>
    internal static bool IsAllowedAddress(string address)
        => Uri.TryCreate(address, UriKind.Absolute, out var uri)
           && string.IsNullOrEmpty(uri.UserInfo)
           && (uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback));

    /// <summary>A provider's error text with the key taken out, in case the provider echoed it.</summary>
    internal static string Hide(string text, string apiKey)
        => string.IsNullOrEmpty(text) || string.IsNullOrEmpty(apiKey) ? text : text.Replace(apiKey, Hidden, StringComparison.Ordinal);
}
