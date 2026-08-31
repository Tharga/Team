using MongoDB.Bson.Serialization.Attributes;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>One handled inbound channel event.</summary>
/// <remarks>
/// Short-lived by design. Slack stops retrying long before the retention window closes, so keeping these
/// forever would grow a collection to no purpose - the TTL index on <see cref="HandledAt"/> removes them.
/// </remarks>
public record SupportEventLedgerEntity : EntityBase
{
    /// <summary>Which channel delivered it, so two channels cannot collide on an id.</summary>
    public required string Source { get; init; }

    /// <summary>The channel's own identifier for the delivery.</summary>
    public required string EventId { get; init; }

    public required DateTime HandledAt { get; init; }
}
