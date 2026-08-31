using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using Tharga.MongoDB;

namespace Tharga.Team.MongoDB;

/// <summary>
/// A support case and its whole transcript, in one document.
/// </summary>
/// <remarks>
/// <b>Messages are embedded rather than stored in their own collection, and that is what buys atomicity.</b>
/// Raising a case creates a case and its first message; closing one sets a status and records why. Embedded,
/// each of those is a single document write and cannot half-happen. In a second collection each would be two
/// writes needing a transaction to keep the model's promise that a case always has a transcript. Team members
/// are embedded in <c>TeamEntityBase.Members</c> for the same reason.
/// <para>
/// <b>What bounds the document, since an embedded array grows.</b> MongoDB's hard limit is 16 MB. Two caps
/// keep this well clear of it, and both are enforced in the store rather than left to hope:
/// <see cref="SupportCaseLimits.MaxMessagesPerCase"/> and
/// <see cref="SupportCaseLimits.MaxMessageLength"/>. At their product the transcript is roughly 5 MB of
/// text, so even a case of maximum-length messages has substantial headroom.
/// </para>
/// <para>
/// The message-length cap is not only about the document size. Support text is exactly where somebody pastes
/// a log file, so an unbounded body is how one message becomes a megabyte.
/// </para>
/// </remarks>
public record SupportCaseEntity : EntityBase
{
    /// <summary>The case id. Distinct from <see cref="EntityBase.Id"/>, which is the document's ObjectId.</summary>
    public required string CaseId { get; init; }

    public required string TeamKey { get; init; }

    public required string AuthorIdentity { get; init; }

    public required string AuthorName { get; init; }

    public required string Subject { get; init; }

    [BsonRepresentation(BsonType.String)]
    public required SupportCaseStatus Status { get; init; }

    public required DateTime CreatedAt { get; init; }

    [BsonIgnoreIfNull]
    public DateTime? ClosedAt { get; init; }

    [BsonIgnoreIfNull]
    public string ClosedBy { get; init; }

    public required SupportMessageEntity[] Messages { get; init; }

    /// <summary>
    /// Whether the newest transcript entry came from the person who raised the case — that is, whether the
    /// case is waiting on support.
    /// </summary>
    /// <remarks>
    /// <b>Denormalized deliberately.</b> The question is "is the last element of an embedded array authored
    /// by the same person as this document's author", which cannot be expressed as a cheap indexed filter -
    /// it needs an aggregation over every case. Maintained at the two places a transcript grows, so the
    /// awaiting-support count stays a plain filter however many cases a team accumulates.
    /// </remarks>
    public bool LastMessageFromAuthor { get; init; }

    [BsonIgnoreIfNull]
    public SupportChannelBindingEntity[] Bindings { get; init; }

    /// <summary>How far each participant has read. Absent until somebody opens the case.</summary>
    [BsonIgnoreIfNull]
    public SupportCaseReadEntity[] Reads { get; init; }
}

/// <summary>How far one person has read a case's transcript.</summary>
/// <remarks>
/// <b>Embedded, and bounded by the number of participants in one case</b> - not one row per user per case
/// across the whole system, which is what a separate collection would become. Members embed in a team and
/// the transcript embeds in a case for the same reason.
/// <para>
/// <b>Keyed on the sequence, not a timestamp.</b> "Read up to entry seven" is exact; "read at 12:04" has to
/// agree with clocks it has no reason to trust, and the transcript is already sequenced because paging needs
/// it to be.
/// </para>
/// <para>
/// <b>Identity is the stable authentication subject</b>, the same value the author fields and the audit trail
/// use. A per-team member key stops resolving when somebody leaves the team, and a case outlives a
/// membership - read state keyed on one would become unattributable exactly when the case is still open.
/// </para>
/// </remarks>
public record SupportCaseReadEntity
{
    public required string Identity { get; init; }

    /// <summary>The highest transcript sequence this person has seen.</summary>
    public required int LastReadSequence { get; init; }

    public required DateTime ReadAt { get; init; }
}

/// <summary>One entry in an embedded transcript.</summary>
public record SupportMessageEntity
{
    public required int Sequence { get; init; }

    [BsonRepresentation(BsonType.String)]
    public required SupportMessageKind Kind { get; init; }

    [BsonIgnoreIfNull]
    public string AuthorIdentity { get; init; }

    [BsonIgnoreIfNull]
    public string AuthorName { get; init; }

    public required string Body { get; init; }

    public required DateTime SentAt { get; init; }

    [BsonRepresentation(BsonType.String)]
    public SupportMessageDelivery Delivery { get; init; }
}

/// <summary>An embedded projection onto an external system. Unused until the channel work lands.</summary>
public record SupportChannelBindingEntity
{
    [BsonRepresentation(BsonType.String)]
    public required SupportChannelType ChannelType { get; init; }

    public required string ExternalId { get; init; }
}
