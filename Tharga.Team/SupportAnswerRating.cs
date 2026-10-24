namespace Tharga.Team;

/// <summary>
/// How the person the assistant answered found an answer.
/// </summary>
/// <remarks>
/// <see cref="None"/> is the zero value, so every answer given before ratings existed, and every answer nobody has
/// rated, reads as unrated rather than as a verdict.
/// </remarks>
public enum SupportAnswerRating
{
    /// <summary>Not rated, or the rating was taken back.</summary>
    None,

    /// <summary>The answer helped.</summary>
    Up,

    /// <summary>The answer did not help.</summary>
    Down
}
