using System.Reflection;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Driver;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// An <see cref="IWorkDayCollection"/> that keeps its documents in memory and applies the filters and updates the store sends, so the merging writes can be run
/// for real, in any order, without a database (Tharga/Team#399).
/// </summary>
/// <remarks>
/// <b>It evaluates the rendered filter and update, so it checks what the store asks the database to do.</b> It understands equality, <c>$gte $lte $and</c>
/// for filters and <c>$set $min $max $bit</c> for updates, including a dotted path into the <c>Slots</c> document, and throws on anything else. A missing
/// field is created, as <c>$bit</c> and the others do in MongoDB. It has no unique index, so the duplicate-key retry is not exercised here.
/// </remarks>
internal sealed class InMemoryWorkDayCollection
{
    private readonly object _gate = new();
    private readonly List<BsonDocument> _documents = [];

    public InMemoryWorkDayCollection()
    {
        Collection = Substitute.For<IWorkDayCollection>();

        Collection.AddAsync(Arg.Any<WorkDayEntity>(), Arg.Any<IClientSessionHandle>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                lock (_gate) _documents.Add(call.Arg<WorkDayEntity>().ToBsonDocument());
                return Task.CompletedTask;
            });

        Collection.GetAsync(Arg.Any<FilterDefinition<WorkDayEntity>>(), Arg.Any<Options<WorkDayEntity>>(), Arg.Any<CancellationToken>())
            .Returns(call => Read(call.Arg<FilterDefinition<WorkDayEntity>>()));

        Collection.UpdateOneAsync(Arg.Any<FilterDefinition<WorkDayEntity>>(), Arg.Any<UpdateDefinition<WorkDayEntity>>(), Arg.Any<OneOption<WorkDayEntity>>(), Arg.Any<IClientSessionHandle>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult(UpdateOne(call.Arg<FilterDefinition<WorkDayEntity>>(), call.Arg<UpdateDefinition<WorkDayEntity>>())));
    }

    /// <summary>What the store is given.</summary>
    public IWorkDayCollection Collection { get; }

    /// <summary>The stored documents, as the database would hold them.</summary>
    public IReadOnlyList<BsonDocument> Documents
    {
        get
        {
            lock (_gate) return [.. _documents.Select(x => x.DeepClone().AsBsonDocument)];
        }
    }

    private async IAsyncEnumerable<WorkDayEntity> Read(FilterDefinition<WorkDayEntity> filter)
    {
        WorkDayEntity[] found;
        lock (_gate) found = [.. _documents.Where(x => Matches(x, Render(filter))).Select(ToEntity)];

        foreach (var entity in found)
        {
            await Task.Yield();
            yield return entity;
        }
    }

    private EntityChangeResult<WorkDayEntity> UpdateOne(FilterDefinition<WorkDayEntity> filter, UpdateDefinition<WorkDayEntity> update)
    {
        lock (_gate)
        {
            var document = _documents.FirstOrDefault(x => Matches(x, Render(filter)));
            if (document == null) return Changed(null, null);

            var before = ToEntity(document);
            Apply(document, update);
            return Changed(before, ToEntity(document));
        }
    }

    private static WorkDayEntity ToEntity(BsonDocument document) => BsonSerializer.Deserialize<WorkDayEntity>(document.DeepClone().AsBsonDocument);

    private static EntityChangeResult<WorkDayEntity> Changed(WorkDayEntity before, WorkDayEntity after)
        => (EntityChangeResult<WorkDayEntity>)typeof(EntityChangeResult<WorkDayEntity>)
            .GetConstructor(BindingFlags.NonPublic | BindingFlags.Instance, [typeof(WorkDayEntity), typeof(WorkDayEntity)])!
            .Invoke([before, after]);

    private static BsonDocument Render(FilterDefinition<WorkDayEntity> filter)
        => filter.Render(new RenderArgs<WorkDayEntity>(BsonSerializer.LookupSerializer<WorkDayEntity>(), BsonSerializer.SerializerRegistry)).AsBsonDocument;

    private static void Apply(BsonDocument document, UpdateDefinition<WorkDayEntity> update)
    {
        var rendered = update.Render(new RenderArgs<WorkDayEntity>(BsonSerializer.LookupSerializer<WorkDayEntity>(), BsonSerializer.SerializerRegistry)).AsBsonDocument;

        foreach (var op in rendered)
        {
            foreach (var field in op.Value.AsBsonDocument)
            {
                switch (op.Name)
                {
                    case "$set":
                        Write(document, field.Name, field.Value);
                        break;
                    case "$max":
                        if (!Has(document, field.Name) || Read(document, field.Name).CompareTo(field.Value) < 0) Write(document, field.Name, field.Value);
                        break;
                    case "$min":
                        if (!Has(document, field.Name) || Read(document, field.Name).CompareTo(field.Value) > 0) Write(document, field.Name, field.Value);
                        break;
                    case "$bit":
                        var or = field.Value.AsBsonDocument["or"].AsInt32;
                        Write(document, field.Name, Has(document, field.Name) ? Read(document, field.Name).AsInt32 | or : or);
                        break;
                    default:
                        throw new NotSupportedException($"The in-memory collection does not understand the update operator {op.Name}.");
                }
            }
        }
    }

    private static bool Has(BsonDocument document, string path)
    {
        var parts = path.Split('.');
        BsonValue current = document;
        foreach (var part in parts)
        {
            if (current is not BsonDocument inner || !inner.Contains(part)) return false;
            current = inner[part];
        }

        return true;
    }

    private static BsonValue Read(BsonDocument document, string path)
        => path.Split('.').Aggregate((BsonValue)document, (current, part) => current.AsBsonDocument[part]);

    private static void Write(BsonDocument document, string path, BsonValue value)
    {
        var parts = path.Split('.');
        var current = document;
        foreach (var part in parts[..^1])
        {
            if (!current.Contains(part)) current[part] = new BsonDocument();
            current = current[part].AsBsonDocument;
        }

        current[parts[^1]] = value;
    }

    private static bool Matches(BsonDocument document, BsonDocument filter)
    {
        foreach (var condition in filter)
        {
            if (condition.Name == "$and")
            {
                if (!condition.Value.AsBsonArray.All(x => Matches(document, x.AsBsonDocument))) return false;
                continue;
            }

            var actual = document.GetValue(condition.Name, BsonNull.Value);
            if (condition.Value is BsonDocument operators && operators.ElementCount > 0 && operators.Names.All(x => x.StartsWith('$')))
            {
                foreach (var op in operators)
                {
                    var ok = op.Name switch
                    {
                        "$eq" => actual.CompareTo(op.Value) == 0,
                        "$gte" => !actual.IsBsonNull && actual.CompareTo(op.Value) >= 0,
                        "$lte" => !actual.IsBsonNull && actual.CompareTo(op.Value) <= 0,
                        _ => throw new NotSupportedException($"The in-memory collection does not understand the filter operator {op.Name}.")
                    };

                    if (!ok) return false;
                }
            }
            else if (actual.CompareTo(condition.Value) != 0)
            {
                return false;
            }
        }

        return true;
    }
}
