using System.Text.Json;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The support contracts serialize by construction — target architecture rule 3.
/// </summary>
/// <remarks>
/// <b>This is what keeps a future <c>Tharga.Team.Client</c> possible</b>, and it costs nothing to hold now.
/// A contract that only works in-process fails the day one of these crosses a wire, which is exactly when it
/// is most expensive to discover.
/// <para>
/// <see cref="SupportMessage.Source"/> is nullable, so both states are asserted: a null that survives is the
/// site-written case, and it is the one a "did it round-trip?" test passes by accident if only the populated
/// value is checked.
/// </para>
/// </remarks>
public class SupportContractSerializationTests
{
    [Fact]
    public void SupportMessage_RoundTrips_WithAChannelSource()
    {
        var message = new SupportMessage
        {
            Sequence = 4,
            Kind = SupportMessageKind.User,
            AuthorIdentity = "sub-1",
            AuthorName = "Alice",
            Body = "The export is empty.",
            SentAt = new DateTime(2026, 9, 1, 8, 30, 0, DateTimeKind.Utc),
            Delivery = SupportMessageDelivery.Sent,
            Source = SupportChannelType.Email
        };

        Assert.Equal(message, RoundTrip(message));
    }

    [Fact]
    public void SupportMessage_RoundTrips_WithNoSource()
    {
        var message = new SupportMessage
        {
            Sequence = 1,
            Kind = SupportMessageKind.User,
            AuthorIdentity = "sub-1",
            AuthorName = "Alice",
            Body = "Raised on the site.",
            SentAt = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
            Delivery = SupportMessageDelivery.NotApplicable
        };

        var restored = RoundTrip(message);

        Assert.Null(restored.Source);
        Assert.Equal(message, restored);
    }

    [Fact]
    public void SupportChannelBinding_RoundTrips_ForEveryChannelType()
    {
        foreach (var channelType in Enum.GetValues<SupportChannelType>())
        {
            var binding = new SupportChannelBinding { ChannelType = channelType, ExternalId = "external-1" };

            Assert.Equal(binding, RoundTrip(binding));
        }
    }

    /// <summary>
    /// Arrays compare by reference, so the suggestions are asserted field by field rather than by record equality.
    /// </summary>
    [Fact]
    public void SupportCase_RoundTrips_WithSuggestedTeams()
    {
        var supportCase = new SupportCase
        {
            Id = "case-1",
            AuthorIdentity = null,
            AuthorName = "anna@example.com",
            Subject = "Export is empty",
            Status = SupportCaseStatus.Open,
            CreatedAt = new DateTime(2026, 10, 17, 9, 0, 0, DateTimeKind.Utc),
            MessageCount = 1,
            SuggestedTeams = [new SupportCaseTeam { Key = "acme", Name = "Acme" }],
            SuggestionReason = SupportTeamSuggestionReason.SenderNotVerified
        };

        var restored = RoundTrip(supportCase);

        Assert.Equal(supportCase.SuggestedTeams[0], Assert.Single(restored.SuggestedTeams));
        Assert.Equal(SupportTeamSuggestionReason.SenderNotVerified, restored.SuggestionReason);
    }

    [Fact]
    public void SupportSender_RoundTrips()
    {
        var sender = new SupportSender { Identity = "sub-1", Name = "Anna", Teams = [new SupportCaseTeam { Key = "acme", Name = "Acme" }] };

        var restored = RoundTrip(sender);

        Assert.Equal("sub-1", restored.Identity);
        Assert.Equal(sender.Teams[0], Assert.Single(restored.Teams));
    }

    private static T RoundTrip<T>(T value)
        => JsonSerializer.Deserialize<T>(JsonSerializer.Serialize(value));
}
