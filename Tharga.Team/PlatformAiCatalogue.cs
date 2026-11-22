namespace Tharga.Team;

/// <summary>A provider the platform has an account with: where it is and which kind it is. Its key is never part of this.</summary>
public sealed record PlatformAiProvider
{
    /// <summary>The platform's own identifier for the provider; models name it.</summary>
    public string Id { get; init; }

    /// <summary>What staff call it.</summary>
    public string Name { get; init; }

    public SupportAiConnectionKind Kind { get; init; }

    public string BaseAddress { get; init; }

    /// <summary>Whether a key is stored. The key itself is never returned, only whether there is one.</summary>
    public bool HasApiKey { get; init; }
}

/// <summary>A model the platform offers teams to choose from, and what a team should know before it does.</summary>
public sealed record PlatformAiModel
{
    /// <summary>The platform's own identifier for the entry; a team's choice names it.</summary>
    public string Id { get; init; }

    /// <summary>The <see cref="PlatformAiProvider.Id"/> it is reached through.</summary>
    public string ProviderId { get; init; }

    /// <summary>The model's identifier at the provider.</summary>
    public string ModelId { get; init; }

    /// <summary>The name teams see.</summary>
    public string DisplayName { get; init; }

    /// <summary>Where the conversation is processed, for example a country or a region.</summary>
    public string ProcessedIn { get; init; }

    /// <summary>Where the provider keeps it afterwards.</summary>
    public string StoredIn { get; init; }

    /// <summary>A link to the provider's data processing agreement.</summary>
    public string AgreementUrl { get; init; }

    /// <summary>Price per million input tokens, in <see cref="Currency"/>; null when not given.</summary>
    public decimal? PriceInput { get; init; }

    /// <summary>Price per million cached input tokens; null when not given.</summary>
    public decimal? PriceCachedInput { get; init; }

    /// <summary>Price per million tokens written to the provider's cache; null when not given, which prices writes as ordinary input.</summary>
    public decimal? PriceCacheWrite { get; init; }

    /// <summary>Price per million output tokens; null when not given.</summary>
    public decimal? PriceOutput { get; init; }

    /// <summary>The currency of the prices, for example <c>USD</c>.</summary>
    public string Currency { get; init; } = "USD";

    /// <summary>Whether teams may choose it. A model that is not offered stays in the list for staff, and a team that had it is answered by the default.</summary>
    public bool Offered { get; init; }
}

/// <summary>The platform's providers and models, and which model answers a team that has not chosen one.</summary>
public sealed record PlatformAiCatalogue
{
    public PlatformAiProvider[] Providers { get; init; } = [];

    public PlatformAiModel[] Models { get; init; } = [];

    /// <summary>The <see cref="PlatformAiModel.Id"/> a team that chose the platform without naming a model is answered by.</summary>
    public string DefaultModelId { get; init; }

    public DateTime? ChangedAt { get; init; }

    public string ChangedBy { get; init; }
}

/// <summary>Setting one provider. The key is kept when <see cref="ApiKey"/> is null and removed when it is empty.</summary>
public sealed record PlatformAiProviderRequest
{
    /// <summary>Which provider to change; null adds one, and the platform names it.</summary>
    public string Id { get; init; }

    public string Name { get; init; }

    public SupportAiConnectionKind Kind { get; init; }

    public string BaseAddress { get; init; }

    /// <summary>A new key; null keeps the stored one; empty removes it.</summary>
    public string ApiKey { get; init; }

    public override string ToString()
        => $"{nameof(PlatformAiProviderRequest)} {{ Id = {Id}, Kind = {Kind}, BaseAddress = {BaseAddress}, ApiKey = {(ApiKey == null ? "null" : "(hidden)")} }}";
}

/// <summary>The models staff offer, replacing the list, and which of them is the default.</summary>
public sealed record PlatformAiModelsRequest
{
    public PlatformAiModel[] Models { get; init; } = [];

    public string DefaultModelId { get; init; }
}

/// <summary>What the platform's staff do with the catalogue. Every operation requires <see cref="SystemSupportScopes.AiCatalogueManage"/>.</summary>
/// <remarks>
/// Operations rather than CRUD, with serializable inputs and outputs, so REST, MCP and the admin screen are the same calls.
/// A key is encrypted when stored, never returned, and never in a problem text.
/// </remarks>
public interface IPlatformAiCatalogueService
{
    /// <summary>The providers (without keys) and the models, with which is the default.</summary>
    Task<PlatformAiCatalogue> GetCatalogueAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds a provider or changes one. A new key is checked against the provider before anything is saved.</summary>
    Task<SupportAiConnectionResult> SetProviderAsync(PlatformAiProviderRequest request, CancellationToken cancellationToken = default);

    /// <summary>Removes a provider, which is refused while a model is reached through it.</summary>
    Task<SupportAiConnectionResult> RemoveProviderAsync(string providerId, CancellationToken cancellationToken = default);

    /// <summary>What the provider's stored key may use, to choose models from.</summary>
    Task<SupportAiModelList> ListProviderModelsAsync(string providerId, CancellationToken cancellationToken = default);

    /// <summary>Replaces the models and the default. Refused when a model names no provider, an identifier repeats, or the default is not an offered model.</summary>
    Task<SupportAiConnectionResult> SetModelsAsync(PlatformAiModelsRequest request, CancellationToken cancellationToken = default);

    /// <summary>Asks the model to answer one short prompt, which spends a few tokens, to show that it works.</summary>
    Task<SupportAiConnectionResult> TestModelAsync(string modelId, CancellationToken cancellationToken = default);
}

/// <summary>The catalogue as it is stored. The one document, so a change is atomic and a read is one read.</summary>
public interface IPlatformAiCatalogueStore
{
    /// <summary>The catalogue, or null when none has been saved.</summary>
    Task<StoredPlatformAiCatalogue> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>Replaces the catalogue.</summary>
    Task SetAsync(StoredPlatformAiCatalogue catalogue, CancellationToken cancellationToken = default);
}

public sealed record StoredPlatformAiCatalogue
{
    public StoredPlatformAiProvider[] Providers { get; init; } = [];

    public PlatformAiModel[] Models { get; init; } = [];

    public string DefaultModelId { get; init; }

    public DateTime ChangedAt { get; init; }

    public string ChangedByIdentity { get; init; }

    public string ChangedByName { get; init; }
}

public sealed record StoredPlatformAiProvider
{
    public string Id { get; init; }

    public string Name { get; init; }

    public SupportAiConnectionKind Kind { get; init; }

    public string BaseAddress { get; init; }

    /// <summary>The key, protected with Data Protection. Null when there is none.</summary>
    public string ProtectedApiKey { get; init; }
}
