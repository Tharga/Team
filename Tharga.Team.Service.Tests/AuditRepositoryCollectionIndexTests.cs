using System.Reflection;
using System.Runtime.CompilerServices;
using global::MongoDB.Driver;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests;

public class AuditRepositoryCollectionIndexTests
{
    private const string TtlIndexName = "Timestamp_TTL";

    [Fact]
    public void NoRetention_DefinesNoTtlIndex_SoEntriesAreKept()
    {
        var names = IndexNames(retentionDays: null);

        Assert.DoesNotContain(TtlIndexName, names);
    }

    [Fact]
    public void ARetention_DefinesTheTtlIndexWithThatExpiry()
    {
        var ttl = Indices(retentionDays: 427).Single(x => x.Options.Name == TtlIndexName);

        Assert.Equal(TimeSpan.FromDays(427), ttl.Options.ExpireAfter);
    }

    private static string[] IndexNames(int? retentionDays) => [.. Indices(retentionDays).Select(x => x.Options.Name)];

    private static IEnumerable<CreateIndexModel<AuditEntryEntity>> Indices(int? retentionDays)
    {
        var collection = (AuditRepositoryCollection)RuntimeHelpers.GetUninitializedObject(typeof(AuditRepositoryCollection));
        typeof(AuditRepositoryCollection).GetField("_retentionDays", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(collection, retentionDays);

        return collection.Indices;
    }
}
