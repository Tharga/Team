using MongoDB.Bson;
using MongoDB.Driver;

namespace Tharga.Team.MongoDB;

/// <summary>MongoDB adapter for <see cref="IContactMailSuppressionStore"/>: one document per address and category, the first kept.</summary>
internal sealed class MongoContactMailSuppressionStore(IContactMailSuppressionCollection collection) : IContactMailSuppressionStore
{
    /// <summary>What the category field holds for an opt-out of every contact mail; no category key can be this, so neither can collide.</summary>
    public const string EveryCategory = "*";

    internal const int AddressesPerQuery = 1000;

    /// <remarks>An insert, and the unique index turns a second one for the same address and category into nothing.</remarks>
    public Task<bool> AddAsync(ContactMailSuppression suppression, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(suppression);

        return collection.TryAddAsync(ToEntity(suppression));
    }

    /// <remarks>Asked in slices of <see cref="AddressesPerQuery"/> addresses, so each query stays small and uses the address index.</remarks>
    public async Task<IReadOnlyList<ContactMailSuppression>> GetAsync(IReadOnlyCollection<string> addresses, CancellationToken cancellationToken = default)
    {
        var found = new List<ContactMailSuppression>();

        foreach (var slice in Normalized(addresses).Chunk(AddressesPerQuery))
        {
            await foreach (var entity in collection.GetAsync(Of(slice), cancellationToken: cancellationToken))
                found.Add(ToSuppression(entity));
        }

        return found;
    }

    internal static FilterDefinition<ContactMailSuppressionEntity> Of(IEnumerable<string> addresses)
        => Builders<ContactMailSuppressionEntity>.Filter.In(x => x.Address, addresses);

    internal static string[] Normalized(IEnumerable<string> addresses)
        => [.. (addresses ?? []).Select(Normalize).Where(x => x != null).Distinct(StringComparer.Ordinal)];

    internal static ContactMailSuppressionEntity ToEntity(ContactMailSuppression suppression) => new()
    {
        Id = ObjectId.GenerateNewId(),
        Address = Normalize(suppression.Address) ?? throw new ArgumentException("An opt-out names an address.", nameof(suppression)),
        Category = string.IsNullOrWhiteSpace(suppression.Category) ? EveryCategory : suppression.Category.Trim(),
        SuppressedAt = suppression.SuppressedAt,
        Source = suppression.Source
    };

    internal static ContactMailSuppression ToSuppression(ContactMailSuppressionEntity entity) => new()
    {
        Address = entity.Address,
        Category = entity.Category == EveryCategory ? null : entity.Category,
        SuppressedAt = DateTime.SpecifyKind(entity.SuppressedAt, DateTimeKind.Utc),
        Source = entity.Source
    };

    private static string Normalize(string address) => string.IsNullOrWhiteSpace(address) ? null : address.Trim().ToLowerInvariant();
}
