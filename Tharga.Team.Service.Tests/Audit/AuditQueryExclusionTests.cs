using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Service.Tests.Audit;

/// <summary>
/// The exclusion half of the audit query. Every other filter is an include list, which cannot say
/// "everything except the log's own readers" without enumerating every category that exists today and
/// remembering to add each new one (Tharga/Team#260).
/// </summary>
public class AuditQueryExclusionTests
{
    [Fact]
    public void AnExcludedScope_BecomesANotInClause()
    {
        var rendered = Render(new AuditQuery { ExcludedScopes = ["audit:read"] });

        Assert.Equal(new BsonArray { "audit:read" }, rendered["ScopeChecked"]["$nin"]);
    }

    [Fact]
    public void AnExcludedEventType_BecomesANotInClause()
    {
        var rendered = Render(new AuditQuery { ExcludedEventTypes = [AuditEventType.ServiceCall] });

        Assert.Equal(new BsonArray { "ServiceCall" }, rendered["EventType"]["$nin"]);
    }

    /// <summary>
    /// The entity stores the enum by name, so the exclusion has to be written by name too. A filter
    /// rendering an ordinal would silently match nothing.
    /// </summary>
    [Fact]
    public void AnExcludedEventType_IsWrittenByNameNotOrdinal()
    {
        var rendered = Render(new AuditQuery { ExcludedEventTypes = [AuditEventType.DataChange] });

        Assert.Equal(BsonType.String, rendered["EventType"]["$nin"].AsBsonArray[0].BsonType);
    }

    /// <summary>
    /// Include and exclude are not alternatives — both apply, so an include list can be narrowed further
    /// rather than one silently discarding the other. The driver folds two constraints on one field into a
    /// single clause carrying both operators.
    /// </summary>
    [Fact]
    public void IncludingAndExcludingScopes_AppliesBoth()
    {
        var rendered = Render(new AuditQuery { Scopes = ["team:read", "audit:read"], ExcludedScopes = ["audit:read"] });

        var scope = rendered["ScopeChecked"].AsBsonDocument;
        Assert.Equal(new BsonArray { "team:read", "audit:read" }, scope["$in"]);
        Assert.Equal(new BsonArray { "audit:read" }, scope["$nin"]);
    }

    [Fact]
    public void NoExclusion_AddsNoClause()
    {
        Assert.Equal(new BsonDocument(), Render(new AuditQuery()));
    }

    [Fact]
    public void AnEmptyExclusion_AddsNoClause()
    {
        Assert.Equal(new BsonDocument(), Render(new AuditQuery { ExcludedScopes = [], ExcludedEventTypes = [] }));
    }

    private static BsonDocument Render(AuditQuery query)
    {
        var registry = BsonSerializer.SerializerRegistry;
        return MongoDbAuditLogger.BuildFilter(query)
            .Render(new RenderArgs<AuditEntryEntity>(registry.GetSerializer<AuditEntryEntity>(), registry));
    }
}
