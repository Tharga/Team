using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Tharga.MongoDB;
using Tharga.MongoDB.Disk;

namespace Tharga.Team.MongoDB;

/// <summary>One person's recorded activity in one team on one day (Tharga/Team#399, phase 3).</summary>
public record WorkDayEntity : EntityBase
{
    /// <summary>The person.</summary>
    public required string UserKey { get; init; }

    /// <summary>The team they were active in.</summary>
    public required string TeamKey { get; init; }

    /// <summary>The day in the configured time zone, as <c>yyyy-MM-dd</c>. Text sorts as a date does, so a range is a range of strings.</summary>
    public required string Day { get; init; }

    /// <summary>The start of the first active minute, in UTC. Only ever moves earlier.</summary>
    public DateTime First { get; init; }

    /// <summary>The start of the last active minute, in UTC. Only ever moves later, and is what the retention counts from.</summary>
    public DateTime Last { get; init; }

    /// <summary>The active minutes per 15-minute slot: the slot's number since the Unix epoch, as text, and a bitmask with bit 0 as the slot's first minute.</summary>
    public Dictionary<string, int> Slots { get; init; } = new();
}

/// <summary>The collection of work days.</summary>
public interface IWorkDayCollection : IDiskRepositoryCollection<WorkDayEntity>;

/// <summary>Work days, one document per person, team and day, default collection name <c>WorkDay</c>; kept for good unless <see cref="ThargaTeamOptions.WorkTimeRetention"/> says otherwise.</summary>
/// <remarks>
/// The unique index is what makes the first write of a day safe across instances: two that both find no document both try to create it, and the loser updates
/// the winner's instead. It also serves a person's days in a team, by day. The expiry index exists only when a retention is configured, and counts from
/// <see cref="WorkDayEntity.Last"/>, so a day is dropped that long after its last activity.
/// </remarks>
public class WorkDayCollection : DiskRepositoryCollectionBase<WorkDayEntity>, IWorkDayCollection
{
    private readonly string _collectionName;
    private readonly TimeSpan? _retention;

    /// <summary>Creates the collection under the configured name.</summary>
    public WorkDayCollection(IMongoDbServiceFactory mongoDbServiceFactory, ILogger<WorkDayCollection> logger, IOptions<ThargaTeamOptions> options = null)
        : base(mongoDbServiceFactory, logger)
    {
        _collectionName = options?.Value.WorkDayCollectionName ?? ThargaTeamOptions.DefaultWorkDayCollectionName;
        _retention = options?.Value.WorkTimeRetention is { } retention && retention > TimeSpan.Zero ? retention : null;
    }

    /// <inheritdoc />
    public override string CollectionName => _collectionName;

    /// <inheritdoc />
    public override IEnumerable<CreateIndexModel<WorkDayEntity>> Indices => IndicesFor(_retention);

    // Apart from the collection, which needs a database to construct, so that the indexes can be read without one.
    internal static IEnumerable<CreateIndexModel<WorkDayEntity>> IndicesFor(TimeSpan? retention)
    {
        yield return new CreateIndexModel<WorkDayEntity>(
            Builders<WorkDayEntity>.IndexKeys.Ascending(x => x.UserKey).Ascending(x => x.TeamKey).Ascending(x => x.Day),
            new CreateIndexOptions { Unique = true, Name = "UserKey_TeamKey_Day" });

        if (retention is { } keep && keep > TimeSpan.Zero)
        {
            yield return new CreateIndexModel<WorkDayEntity>(
                Builders<WorkDayEntity>.IndexKeys.Ascending(x => x.Last),
                new CreateIndexOptions { ExpireAfter = keep, Name = "Last_ttl" });
        }
    }
}
