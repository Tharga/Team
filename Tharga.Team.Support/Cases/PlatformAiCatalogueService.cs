using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.AI;
using Tharga.Team.Service;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// The platform's AI catalogue: the providers the platform has accounts with, the models teams may choose from, and the default.
/// </summary>
/// <remarks>
/// <b>A key is protected before the store sees it</b>, under a purpose of its own, and is unprotected in one place
/// (<see cref="KeyOf"/>), to reach the provider. It is never returned and never in a problem text.
/// <para>
/// <b>A stored key is only sent where it was entered for</b>: leaving the key out keeps the stored one only while the
/// provider's kind and address are unchanged, as for a team's own connection.
/// </para>
/// Authorization is the decorator's, and auditing the decorator around it.
/// </remarks>
internal sealed class PlatformAiCatalogueService(
    IPlatformAiCatalogueStore store,
    IDataProtectionProvider dataProtection,
    TeamAuthorizer authorizer,
    TimeProvider timeProvider,
    ISupportChatClientFactory factory = null) : IPlatformAiCatalogueService
{
    internal const string ProtectionPurpose = "Tharga.Team.Support.PlatformAiCatalogue.ApiKey.v1";

    private readonly IDataProtector _protector = dataProtection.CreateProtector(ProtectionPurpose);

    public async Task<PlatformAiCatalogue> GetCatalogueAsync(CancellationToken cancellationToken = default)
    {
        var stored = await store.GetAsync(cancellationToken);
        return stored == null ? new PlatformAiCatalogue() : ToCatalogue(stored);
    }

    internal static PlatformAiCatalogue ToCatalogue(StoredPlatformAiCatalogue stored) => new()
    {
        Providers = [.. stored.Providers.Select(x => new PlatformAiProvider
        {
            Id = x.Id,
            Name = x.Name,
            Kind = x.Kind,
            BaseAddress = x.BaseAddress,
            HasApiKey = x.ProtectedApiKey != null
        })],
        Models = stored.Models,
        DefaultModelId = stored.DefaultModelId,
        ChangedAt = stored.ChangedAt,
        ChangedBy = stored.ChangedByName
    };

    public async Task<SupportAiConnectionResult> SetProviderAsync(PlatformAiProviderRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null) return SupportAiConnectionResult.Failure("Give the provider.");
        if (string.IsNullOrWhiteSpace(request.Name)) return SupportAiConnectionResult.Failure("Give the provider a name.");

        var defaults = factory?.Providers.FirstOrDefault(x => x.Kind == request.Kind);
        if (defaults == null) return SupportAiConnectionResult.Failure($"{request.Kind} is not offered here.");

        var baseAddress = SupportAiConnectionService.Normalize(string.IsNullOrWhiteSpace(request.BaseAddress) ? defaults.BaseAddress : request.BaseAddress);
        if (string.IsNullOrEmpty(baseAddress)) return SupportAiConnectionResult.Failure("Enter the address.");
        if (!SupportAiConnectionService.IsAllowedAddress(baseAddress)) return SupportAiConnectionResult.Failure("The address must be https, or http on this machine.");

        var catalogue = await store.GetAsync(cancellationToken) ?? new StoredPlatformAiCatalogue();
        var existing = string.IsNullOrWhiteSpace(request.Id) ? null : catalogue.Providers.FirstOrDefault(x => x.Id == request.Id);
        if (!string.IsNullOrWhiteSpace(request.Id) && existing == null) return SupportAiConnectionResult.Failure("There is no such provider.");

        string apiKey;
        string protectedKey;
        if (request.ApiKey == null)
        {
            // Keep what is stored, but only where it was entered for.
            var sameTarget = existing != null && existing.Kind == request.Kind
                && string.Equals(SupportAiConnectionService.Normalize(existing.BaseAddress), baseAddress, StringComparison.OrdinalIgnoreCase);
            protectedKey = sameTarget ? existing.ProtectedApiKey : null;
            apiKey = null;
            if (protectedKey == null && defaults.RequiresApiKey) return SupportAiConnectionResult.Failure("Enter the key.");
        }
        else if (request.ApiKey.Trim().Length == 0)
        {
            if (defaults.RequiresApiKey) return SupportAiConnectionResult.Failure("This provider needs a key, so it cannot be removed.");
            apiKey = null;
            protectedKey = null;
        }
        else
        {
            apiKey = request.ApiKey.Trim();
            protectedKey = _protector.Protect(apiKey);
        }

        // A new key is proven against the provider before anything is saved; listing the models spends no tokens.
        if (apiKey != null)
        {
            var models = await factory.ListModelsAsync(new SupportAiEndpoint(request.Kind, baseAddress, apiKey, null), cancellationToken);
            if (models.Problem != null) return SupportAiConnectionResult.Failure(SupportAiConnectionService.Hide(models.Problem, apiKey));
        }

        var provider = new StoredPlatformAiProvider
        {
            Id = existing?.Id ?? Guid.NewGuid().ToString("N"),
            Name = request.Name.Trim(),
            Kind = request.Kind,
            BaseAddress = baseAddress,
            ProtectedApiKey = protectedKey
        };

        await SaveAsync(catalogue with
        {
            Providers = [.. catalogue.Providers.Where(x => x.Id != provider.Id), provider]
        }, cancellationToken);

        return SupportAiConnectionResult.Success();
    }

    public async Task<SupportAiConnectionResult> RemoveProviderAsync(string providerId, CancellationToken cancellationToken = default)
    {
        var catalogue = await store.GetAsync(cancellationToken);
        if (catalogue == null || catalogue.Providers.All(x => x.Id != providerId)) return SupportAiConnectionResult.Failure("There is no such provider.");

        var using_ = catalogue.Models.Where(x => x.ProviderId == providerId).Select(x => x.DisplayName ?? x.ModelId).ToArray();
        if (using_.Length > 0) return SupportAiConnectionResult.Failure($"Models are reached through this provider: {string.Join(", ", using_)}. Remove them first.");

        await SaveAsync(catalogue with { Providers = [.. catalogue.Providers.Where(x => x.Id != providerId)] }, cancellationToken);

        return SupportAiConnectionResult.Success();
    }

    public async Task<SupportAiModelList> ListProviderModelsAsync(string providerId, CancellationToken cancellationToken = default)
    {
        var catalogue = await store.GetAsync(cancellationToken);
        var provider = catalogue?.Providers.FirstOrDefault(x => x.Id == providerId);
        if (provider == null) return new SupportAiModelList { Problem = "There is no such provider." };

        var (apiKey, problem) = KeyOf(provider);
        if (problem != null) return new SupportAiModelList { Problem = problem };

        var models = await factory.ListModelsAsync(new SupportAiEndpoint(provider.Kind, provider.BaseAddress, apiKey, null), cancellationToken);
        return models.Problem == null ? models : models with { Problem = SupportAiConnectionService.Hide(models.Problem, apiKey) };
    }

    public async Task<SupportAiConnectionResult> SetModelsAsync(PlatformAiModelsRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null) return SupportAiConnectionResult.Failure("Give the models.");

        var catalogue = await store.GetAsync(cancellationToken) ?? new StoredPlatformAiCatalogue();
        var models = request.Models ?? [];

        var problem = Validate(models, request.DefaultModelId, catalogue.Providers);
        if (problem != null) return SupportAiConnectionResult.Failure(problem);

        await SaveAsync(catalogue with
        {
            Models = [.. models.Select(x => x with
            {
                Id = x.Id.Trim(),
                ModelId = x.ModelId.Trim(),
                DisplayName = string.IsNullOrWhiteSpace(x.DisplayName) ? x.ModelId.Trim() : x.DisplayName.Trim(),
                Currency = string.IsNullOrWhiteSpace(x.Currency) ? "USD" : x.Currency.Trim().ToUpperInvariant()
            })],
            DefaultModelId = string.IsNullOrWhiteSpace(request.DefaultModelId) ? null : request.DefaultModelId.Trim()
        }, cancellationToken);

        return SupportAiConnectionResult.Success();
    }

    internal static string Validate(PlatformAiModel[] models, string defaultModelId, StoredPlatformAiProvider[] providers)
    {
        foreach (var model in models)
        {
            if (string.IsNullOrWhiteSpace(model.Id)) return "Every model needs an identifier.";
            if (string.IsNullOrWhiteSpace(model.ModelId)) return $"'{model.Id}' needs the provider's model name.";
            if (providers.All(x => x.Id != model.ProviderId)) return $"'{model.DisplayName ?? model.Id}' names a provider that does not exist.";
            if (new[] { model.PriceInput, model.PriceCachedInput, model.PriceCacheWrite, model.PriceOutput }.Any(x => x < 0)) return $"'{model.DisplayName ?? model.Id}' has a negative price.";
            if (!string.IsNullOrWhiteSpace(model.AgreementUrl)
                && !(Uri.TryCreate(model.AgreementUrl.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(uri.UserInfo)))
                return $"The agreement link of '{model.DisplayName ?? model.Id}' must be an https address.";
        }

        var repeated = models.GroupBy(x => x.Id?.Trim(), StringComparer.Ordinal).FirstOrDefault(x => x.Count() > 1);
        if (repeated != null) return $"The identifier '{repeated.Key}' is used twice.";

        var offered = models.Where(x => x.Offered).ToArray();
        if (offered.Length == 0) return string.IsNullOrWhiteSpace(defaultModelId) ? null : "The default must be a model that is offered.";
        if (string.IsNullOrWhiteSpace(defaultModelId)) return "Choose which offered model is the default.";

        return offered.Any(x => x.Id.Trim() == defaultModelId.Trim()) ? null : "The default must be a model that is offered.";
    }

    public async Task<SupportAiConnectionResult> TestModelAsync(string modelId, CancellationToken cancellationToken = default)
    {
        var catalogue = await store.GetAsync(cancellationToken);
        var model = catalogue?.Models.FirstOrDefault(x => x.Id == modelId);
        var provider = model == null ? null : catalogue.Providers.FirstOrDefault(x => x.Id == model.ProviderId);
        if (provider == null) return SupportAiConnectionResult.Failure("There is no such model.");

        var (apiKey, problem) = KeyOf(provider);
        if (problem != null) return SupportAiConnectionResult.Failure(problem);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(SupportAiConnectionService.TestTimeout);

        try
        {
            using var chat = factory.Create(new SupportAiEndpoint(provider.Kind, provider.BaseAddress, apiKey, model.ModelId));
            var response = await chat.GetResponseAsync(SupportAiConnectionService.TestPrompt, new ChatOptions { MaxOutputTokens = SupportAiConnectionService.TestMaxOutputTokens }, deadline.Token);
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
            return SupportAiConnectionResult.Failure($"The model could not answer: {SupportAiConnectionService.Hide(ex.Message, apiKey)}");
        }
    }

    /// <summary>The provider's key in clear, or why it cannot be had. The one place a catalogue key is unprotected for the service.</summary>
    private (string ApiKey, string Problem) KeyOf(StoredPlatformAiProvider provider)
    {
        if (provider.ProtectedApiKey == null) return (null, null);

        try
        {
            return (_protector.Unprotect(provider.ProtectedApiKey), null);
        }
        catch (CryptographicException)
        {
            return (null, "The stored key can no longer be read. Enter it again.");
        }
    }

    private async Task SaveAsync(StoredPlatformAiCatalogue catalogue, CancellationToken cancellationToken)
        => await store.SetAsync(catalogue with
        {
            ChangedAt = timeProvider.GetUtcNow().UtcDateTime,
            ChangedByIdentity = await authorizer.GetSubjectAsync(),
            ChangedByName = await authorizer.GetDisplayNameAsync()
        }, cancellationToken);
}
