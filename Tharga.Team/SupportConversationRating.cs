namespace Tharga.Team;

/// <summary>
/// How the person an assistant conversation answered rated the conversation as a whole.
/// </summary>
/// <remarks>
/// Kept on the case as <see cref="SupportCase.ConversationRating"/>, beside the ratings of single answers, because a
/// conversation can let somebody down even when each answer in it looked right on its own. Rating again replaces it.
/// </remarks>
public sealed record SupportConversationRating
{
    /// <summary><see cref="SupportAnswerRating.Up"/> or <see cref="SupportAnswerRating.Down"/>.</summary>
    public required SupportAnswerRating Rating { get; init; }

    /// <summary>Why, in the person's own words, or <c>null</c> when they gave no reason.</summary>
    public string Comment { get; init; }

    /// <summary>When the conversation was rated, in UTC.</summary>
    public required DateTime RatedAt { get; init; }

    /// <summary>The authentication subject of whoever rated it, which is always the person who raised the case.</summary>
    public required string RatedBy { get; init; }
}
