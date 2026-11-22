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
    /// Whether the person the assistant answered is asked, under the conversation, how the assistant did in it as a whole,
    /// with a comment on thumbs-down, and the feedback view lists the conversations rated. Default <c>false</c>: a host
    /// turns it on.
    /// </summary>
    public bool RateConversations { get; set; }

    /// <summary>
    /// Who sees what an answer took, folded under it: the service calls the assistant made and the tokens the model
    /// reported. Default <see cref="SupportAnswerDetailsAudience.Everyone"/>.
    /// </summary>
    public SupportAnswerDetailsAudience AnswerDetailsShownTo { get; set; } = SupportAnswerDetailsAudience.Everyone;

    /// <summary>
    /// Whether the assistant tab of <c>SupportPanel</c> shows how much of the team's allowance is spent, as a bar. Default <c>true</c>.
    /// </summary>
    /// <remarks>
    /// <b>The bar only ever shows where there is a limit</b>: a team on its own provider, or with no limit set, has none. Turning it off hides the bar and nothing
    /// else; the assistant still stops answering, and says why, when the allowance is used up.
    /// </remarks>
    public bool ShowAllowance { get; set; } = true;
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
