using System.Reflection;
using System.Text.Json;

namespace Tharga.Team.Support.Tests;

/// <summary>
/// The rated conversation a host delivers, in the one JSON shape every host sends.
/// </summary>
public class SupportConversationFeedbackJsonTests
{
    private static readonly DateTime RatedAt = new(2026, 11, 20, 14, 30, 0, DateTimeKind.Utc);

    private static SupportConversationFeedback Feedback() => new()
    {
        CaseId = "case-1",
        TeamKey = "team-a",
        Rating = SupportAnswerRating.Down,
        Comment = "Den förstod inte frågan <alls>.",
        RatedAt = RatedAt,
        Culture = "sv-SE",
        Messages =
        [
            new SupportConversationFeedbackMessage { Sequence = 1, Kind = SupportMessageKind.User, SentAt = RatedAt.AddMinutes(-5), Body = "Hur länge sparas betyg?" },
            new SupportConversationFeedbackMessage
            {
                Sequence = 2,
                Kind = SupportMessageKind.Assistant,
                SentAt = RatedAt.AddMinutes(-4),
                Body = "I could not find it.",
                Calls = [new SupportConversationFeedbackCall { Name = "list_retention_rules", Succeeded = true }, new SupportConversationFeedbackCall { Name = "read_manual", Succeeded = false }]
            }
        ]
    };

    [Fact]
    public void Serialize_NamesPropertiesInCamelCase_AndEnumsByName()
    {
        using var json = JsonDocument.Parse(SupportConversationFeedbackJson.Serialize(Feedback()));
        var root = json.RootElement;

        Assert.Equal("case-1", root.GetProperty("caseId").GetString());
        Assert.Equal("team-a", root.GetProperty("teamKey").GetString());
        Assert.Equal("Down", root.GetProperty("rating").GetString());
        Assert.Equal("sv-SE", root.GetProperty("culture").GetString());
        var answer = root.GetProperty("messages")[1];
        Assert.Equal("Assistant", answer.GetProperty("kind").GetString());
        Assert.Equal("read_manual", answer.GetProperty("calls")[1].GetProperty("name").GetString());
        Assert.False(answer.GetProperty("calls")[1].GetProperty("succeeded").GetBoolean());
    }

    [Fact]
    public void Serialize_KeepsLettersReadable_AndStillEscapesMarkup()
    {
        var json = SupportConversationFeedbackJson.Serialize(Feedback());

        Assert.Contains("förstod", json);
        Assert.DoesNotContain("<alls>", json);
    }

    [Fact]
    public void WhatIsWritten_ReadsBack()
    {
        var feedback = Feedback();

        var restored = SupportConversationFeedbackJson.Deserialize(SupportConversationFeedbackJson.Serialize(feedback));

        Assert.Equal(feedback.Comment, restored.Comment);
        Assert.Equal(feedback.RatedAt, restored.RatedAt);
        Assert.Equal(feedback.Messages[1].Calls, restored.Messages[1].Calls);
        Assert.Equal(feedback.Messages[0] with { Calls = [] }, restored.Messages[0] with { Calls = [] });
    }

    [Theory]
    [InlineData(typeof(SupportConversationFeedbackCall), new[] { "Name", "Succeeded" })]
    [InlineData(typeof(SupportConversationFeedbackMessage), new[] { "Sequence", "Kind", "AsSupport", "SentAt", "Body", "Calls" })]
    public void TheShape_CarriesNoArgumentsResultsOrInternalComments(Type type, string[] allowed)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance).Select(x => x.Name).Where(x => x != "EqualityContract");

        Assert.Equal(allowed.Order(), properties.Order());
    }
}
