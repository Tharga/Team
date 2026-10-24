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
    /// were rated. Default <c>true</c>.
    /// </summary>
    public bool RateAnswers { get; set; } = true;

    /// <summary>
    /// Who sees the service calls the assistant made, listed under its answers. Default
    /// <see cref="SupportServiceCallsAudience.Everyone"/>.
    /// </summary>
    public SupportServiceCallsAudience ServiceCallsShownTo { get; set; } = SupportServiceCallsAudience.Everyone;
}

/// <summary>Who sees the service calls under the assistant's answers.</summary>
public enum SupportServiceCallsAudience
{
    /// <summary>The person answered, and support.</summary>
    Everyone,

    /// <summary>Support only: whoever reads the case from the support side.</summary>
    Support,

    /// <summary>Nobody.</summary>
    Nobody
}
