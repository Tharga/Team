using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// <see cref="SupportMessageEntity.Source"/> is stored by name, and omitted entirely when a message was
/// written through the application rather than arriving from a channel.
/// </summary>
/// <remarks>
/// <b>The stored BSON type is the assertion, not the round-trip.</b> The driver reads <c>Int32</c>,
/// <c>Int64</c> and <c>String</c> back regardless of the configured representation, so a round-trip passes
/// just as happily on an ordinal — and an ordinal here would silently re-grade every stored message the day
/// a member is inserted into <see cref="SupportChannelType"/>. Which is a live risk rather than a
/// hypothetical: <c>Email</c> was appended to that enum by this feature.
/// </remarks>
public class SupportMessageSourceSerializationTests
{
    private static SupportMessageEntity NewMessage(SupportChannelType? source) => new()
    {
        Sequence = 1,
        Kind = SupportMessageKind.User,
        Body = "Anything.",
        SentAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
        Source = source
    };

    [Fact]
    public void Source_is_stored_as_a_string_not_an_ordinal()
    {
        var document = NewMessage(SupportChannelType.Email).ToBsonDocument();

        Assert.Equal(BsonType.String, document[nameof(SupportMessageEntity.Source)].BsonType);
        Assert.Equal(nameof(SupportChannelType.Email), document[nameof(SupportMessageEntity.Source)].AsString);
    }

    [Fact]
    public void Source_is_omitted_when_the_message_came_from_no_channel()
    {
        var document = NewMessage(null).ToBsonDocument();

        Assert.False(document.Contains(nameof(SupportMessageEntity.Source)));
    }

    [Fact]
    public void Source_round_trips_through_the_serializer()
    {
        foreach (var channelType in Enum.GetValues<SupportChannelType>())
        {
            var restored = BsonSerializer.Deserialize<SupportMessageEntity>(NewMessage(channelType).ToBsonDocument());

            Assert.Equal(channelType, restored.Source);
        }

        Assert.Null(BsonSerializer.Deserialize<SupportMessageEntity>(NewMessage(null).ToBsonDocument()).Source);
    }
}
