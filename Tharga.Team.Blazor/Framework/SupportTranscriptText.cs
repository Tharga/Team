namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>SupportTranscript</c> — the transcript both back-office queues share.
/// </summary>
/// <remarks>
/// Its own catalogue rather than a key borrowed from either queue, because the component is rendered by both
/// and a key owned by one of them would be a dependency in the wrong direction.
/// </remarks>
public static class SupportTranscriptText
{
    /// <summary>
    /// Marks an entry an assistant wrote rather than a person.
    /// </summary>
    /// <remarks>
    /// Shown to support staff, not to the customer: an agent picking up a case needs to know what the
    /// customer has already been told by an assistant, because it carries different weight from what a
    /// colleague told them.
    /// </remarks>
    public static readonly TextKey Assistant = new("team.support.transcript.assistant", "Assistant");

    /// <summary>Names the icon on an answer from a person on the support side rather than the one who raised the case.</summary>
    public static readonly TextKey Support = new("team.support.transcript.support", "Support");

    /// <summary>Under an answer the case's channels have not taken yet.</summary>
    public static readonly TextKey NotSentYet = new("team.support.transcript.notSentYet", "Not sent yet");

    /// <summary>Under an answer a channel refused or that bounced; the reason follows it.</summary>
    public static readonly TextKey NotDelivered = new("team.support.transcript.notDelivered", "Not delivered");

    /// <summary>The button that sends an undelivered answer again.</summary>
    public static readonly TextKey SendAgain = new("team.support.transcript.sendAgain", "Send again");

    /// <summary>Read out for the dots shown while somebody on the other side is writing; <c>{0}</c> is their name.</summary>
    public static readonly TextKey Typing = new("team.support.transcript.typing", "{0} is typing…");

    /// <summary>Under the assistant's dots or text while it works: which call to the model it is on, <c>{0}</c>.</summary>
    public static readonly TextKey Round = new("team.support.transcript.round", "Round {0}");

    /// <summary>The same while it runs tools: <c>{0}</c> is the round, <c>{1}</c> the tools' names.</summary>
    public static readonly TextKey RoundTools = new("team.support.transcript.roundTools", "Round {0}: {1}");

    /// <summary>The button that rates one of the assistant's answers as helpful, or takes that back.</summary>
    public static readonly TextKey Helpful = new("team.support.transcript.helpful", "Helpful");

    /// <summary>The button that rates one of the assistant's answers as not helpful, or takes that back.</summary>
    public static readonly TextKey NotHelpful = new("team.support.transcript.notHelpful", "Not helpful");

    /// <summary>Marks an answer the person it answered found helpful, where it cannot be rated, as for support.</summary>
    public static readonly TextKey RatedHelpful = new("team.support.transcript.ratedHelpful", "Rated helpful");

    /// <summary>Marks an answer the person it answered found unhelpful, where it cannot be rated.</summary>
    public static readonly TextKey RatedNotHelpful = new("team.support.transcript.ratedNotHelpful", "Rated not helpful");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Assistant, Support, NotSentYet, NotDelivered, SendAgain, Typing, Round, RoundTools, Helpful, NotHelpful, RatedHelpful, RatedNotHelpful];
}
