using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Unicode;

namespace Tharga.Team;

/// <summary>
/// The one JSON shape of <see cref="SupportConversationFeedback"/>, so every host delivers rated conversations alike.
/// </summary>
/// <remarks>
/// Indented, property names in camelCase and enums by name. Letters of every script are written as they are, so the
/// conversation reads in a mail; markup characters stay escaped.
/// </remarks>
public static class SupportConversationFeedbackJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
        Converters = { new JsonStringEnumConverter() }
    };

    /// <summary>Writes <paramref name="feedback"/> as JSON.</summary>
    public static string Serialize(SupportConversationFeedback feedback)
    {
        ArgumentNullException.ThrowIfNull(feedback);

        return JsonSerializer.Serialize(feedback, Options);
    }

    /// <summary>Reads back what <see cref="Serialize"/> wrote.</summary>
    public static SupportConversationFeedback Deserialize(string json) => JsonSerializer.Deserialize<SupportConversationFeedback>(json, Options);
}
