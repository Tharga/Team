using MongoDB.Bson;

namespace Tharga.Team.Service.Audit;

internal enum AuditRetentionIndexAction
{
    None,
    Create,
    Change,
    Drop
}

internal readonly record struct AuditRetentionIndexState(bool Exists, long? ExpireAfterSeconds);

/// <summary>
/// What it takes to make the <c>Timestamp_TTL</c> index of the audit collection match
/// <see cref="AuditOptions.RetentionDays"/>. Kept apart from the MongoDB calls so the decision can be tested
/// without a database.
/// </summary>
internal static class AuditRetentionIndex
{
    public const string CollectionName = "AuditLog";
    public const string Name = "Timestamp_TTL";
    public const string TimestampField = "Timestamp";

    private const string IndexNameField = "name";
    private const string ExpireAfterSecondsField = "expireAfterSeconds";

    public static AuditRetentionIndexAction Decide(AuditRetentionIndexState existing, TimeSpan? wanted)
    {
        if (!wanted.HasValue) return existing.Exists ? AuditRetentionIndexAction.Drop : AuditRetentionIndexAction.None;
        if (!existing.Exists) return AuditRetentionIndexAction.Create;

        return existing.ExpireAfterSeconds == (long)wanted.Value.TotalSeconds ? AuditRetentionIndexAction.None : AuditRetentionIndexAction.Change;
    }

    public static AuditRetentionIndexState Find(IEnumerable<BsonDocument> indexes)
    {
        var index = indexes.FirstOrDefault(x => x.TryGetValue(IndexNameField, out var name) && name.IsString && name.AsString == Name);
        if (index == null) return new AuditRetentionIndexState(false, null);

        var seconds = index.TryGetValue(ExpireAfterSecondsField, out var value) && value.IsNumeric ? value.ToInt64() : (long?)null;
        return new AuditRetentionIndexState(true, seconds);
    }

    public static BsonDocument ChangeCommand(TimeSpan expireAfter)
        => new()
        {
            { "collMod", CollectionName },
            { "index", new BsonDocument { { IndexNameField, Name }, { ExpireAfterSecondsField, (long)expireAfter.TotalSeconds } } }
        };
}
