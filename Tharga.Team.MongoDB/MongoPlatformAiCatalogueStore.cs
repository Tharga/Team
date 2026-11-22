using MongoDB.Driver;

namespace Tharga.Team.MongoDB;

internal sealed class MongoPlatformAiCatalogueStore(IPlatformAiCatalogueCollection collection) : IPlatformAiCatalogueStore
{
    public async Task<StoredPlatformAiCatalogue> GetAsync(CancellationToken cancellationToken = default)
    {
        var entity = await collection.GetOneAsync(x => x.Name == PlatformAiCatalogueEntity.Singleton);
        return entity == null ? null : ToRecord(entity);
    }

    /// <remarks>The whole document is replaced, so a change is atomic. Two staff saving at once: the later wins.</remarks>
    public async Task SetAsync(StoredPlatformAiCatalogue catalogue, CancellationToken cancellationToken = default)
    {
        var filter = Builders<PlatformAiCatalogueEntity>.Filter.Eq(x => x.Name, PlatformAiCatalogueEntity.Singleton);
        var update = Builders<PlatformAiCatalogueEntity>.Update
            .Set(x => x.Providers, ToEntity(catalogue).Providers)
            .Set(x => x.Models, ToEntity(catalogue).Models)
            .Set(x => x.DefaultModelId, catalogue.DefaultModelId)
            .Set(x => x.ChangedAt, catalogue.ChangedAt)
            .Set(x => x.ChangedByIdentity, catalogue.ChangedByIdentity)
            .Set(x => x.ChangedByName, catalogue.ChangedByName);

        if ((await collection.UpdateOneAsync(filter, update))?.Before != null) return;

        if (await collection.TryAddAsync(ToEntity(catalogue))) return;

        // A racing insert won; if the update still finds nothing, the insert failed for another reason.
        if ((await collection.UpdateOneAsync(filter, update))?.Before == null)
            throw new InvalidOperationException("The platform AI catalogue could not be saved.");
    }

    internal static StoredPlatformAiCatalogue ToRecord(PlatformAiCatalogueEntity entity) => new()
    {
        Providers = [.. (entity.Providers ?? []).Select(x => new StoredPlatformAiProvider
        {
            Id = x.Id,
            Name = x.Name,
            Kind = x.Kind,
            BaseAddress = x.BaseAddress,
            ProtectedApiKey = x.ProtectedApiKey
        })],
        Models = [.. (entity.Models ?? []).Select(x => new PlatformAiModel
        {
            Id = x.Id,
            ProviderId = x.ProviderId,
            ModelId = x.ModelId,
            DisplayName = x.DisplayName,
            ProcessedIn = x.ProcessedIn,
            StoredIn = x.StoredIn,
            AgreementUrl = x.AgreementUrl,
            PriceInput = x.PriceInput,
            PriceCachedInput = x.PriceCachedInput,
            PriceCacheWrite = x.PriceCacheWrite,
            PriceOutput = x.PriceOutput,
            Currency = x.Currency ?? "USD",
            Offered = x.Offered
        })],
        DefaultModelId = entity.DefaultModelId,
        ChangedAt = entity.ChangedAt,
        ChangedByIdentity = entity.ChangedByIdentity,
        ChangedByName = entity.ChangedByName
    };

    internal static PlatformAiCatalogueEntity ToEntity(StoredPlatformAiCatalogue record) => new()
    {
        Providers = [.. (record.Providers ?? []).Select(x => new PlatformAiProviderEntity
        {
            Id = x.Id,
            Name = x.Name,
            Kind = x.Kind,
            BaseAddress = x.BaseAddress,
            ProtectedApiKey = x.ProtectedApiKey
        })],
        Models = [.. (record.Models ?? []).Select(x => new PlatformAiModelEntity
        {
            Id = x.Id,
            ProviderId = x.ProviderId,
            ModelId = x.ModelId,
            DisplayName = x.DisplayName,
            ProcessedIn = x.ProcessedIn,
            StoredIn = x.StoredIn,
            AgreementUrl = x.AgreementUrl,
            PriceInput = x.PriceInput,
            PriceCachedInput = x.PriceCachedInput,
            PriceCacheWrite = x.PriceCacheWrite,
            PriceOutput = x.PriceOutput,
            Currency = x.Currency,
            Offered = x.Offered
        })],
        DefaultModelId = record.DefaultModelId,
        ChangedAt = record.ChangedAt,
        ChangedByIdentity = record.ChangedByIdentity,
        ChangedByName = record.ChangedByName
    };
}
