namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// What <c>SupportPanel</c> and the support views show beside the assistant's answers.
/// </summary>
/// <remarks>
/// <b>Display only.</b> Ratings already given and the service calls kept with an answer stay where they are; turning
/// these off hides them, and turning them on again shows them.
/// </remarks>
public sealed class SupportDisplayOptions
{
    /// <summary>
    /// Whether the person the assistant answered can rate its answers with thumbs up or down, and support sees how they
    /// were rated. Default <c>false</c>: a host turns it on.
    /// </summary>
    public bool RateAnswers { get; set; }

    /// <summary>
    /// Who sees what an answer took, folded under it: the service calls the assistant made and the tokens the model
    /// reported. Default <see cref="SupportAnswerDetailsAudience.Everyone"/>.
    /// </summary>
    public SupportAnswerDetailsAudience AnswerDetailsShownTo { get; set; } = SupportAnswerDetailsAudience.Everyone;
}

/// <summary>Who sees the details under the assistant's answers: its service calls and its tokens.</summary>
public enum SupportAnswerDetailsAudience
{
    /// <summary>The person answered, and support.</summary>
    Everyone,

    /// <summary>Support only: whoever reads the case from the support side.</summary>
    Support,

    /// <summary>Nobody.</summary>
    Nobody
}
