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

    /// <summary>Beside the assistant's dots while a service call runs: <c>{0}</c> is which one, as a word from <see cref="LookupOrdinals"/>.</summary>
    public static readonly TextKey Lookup = new("team.support.transcript.lookup", "{0} lookup…");

    /// <summary>The words <see cref="Lookup"/> counts with, comma-separated, from the first; past the last, <see cref="LookupNumbered"/> is used.</summary>
    public static readonly TextKey LookupOrdinals = new("team.support.transcript.lookupOrdinals",
        "First,Second,Third,Fourth,Fifth,Sixth,Seventh,Eighth,Ninth,Tenth");

    /// <summary>Beside the dots for a lookup past the words in <see cref="LookupOrdinals"/>: <c>{0}</c> is its number.</summary>
    public static readonly TextKey LookupNumbered = new("team.support.transcript.lookupNumbered", "Lookup {0}…");

    /// <summary>Opens the list under an answer of the one service call the assistant made for it.</summary>
    public static readonly TextKey ServiceCall = new("team.support.transcript.serviceCall", "1 service call");

    /// <summary>Opens the list under an answer of the service calls the assistant made for it: <c>{0}</c> is how many.</summary>
    public static readonly TextKey ServiceCalls = new("team.support.transcript.serviceCalls", "{0} service calls");

    /// <summary>The button that rates one of the assistant's answers as helpful, or takes that back.</summary>
    public static readonly TextKey Helpful = new("team.support.transcript.helpful", "Helpful");

    /// <summary>The button that rates one of the assistant's answers as not helpful, or takes that back.</summary>
    public static readonly TextKey NotHelpful = new("team.support.transcript.notHelpful", "Not helpful");

    /// <summary>Marks an answer the person it answered found helpful, where it cannot be rated, as for support.</summary>
    public static readonly TextKey RatedHelpful = new("team.support.transcript.ratedHelpful", "Rated helpful");

    /// <summary>Marks an answer the person it answered found unhelpful, where it cannot be rated.</summary>
    public static readonly TextKey RatedNotHelpful = new("team.support.transcript.ratedNotHelpful", "Rated not helpful");

    /// <summary>To the member, once their message to a person has waited a while with no answer.</summary>
    public static readonly TextKey NoAnswerYet = new("team.support.transcript.noAnswerYet",
        "Nobody from support has answered yet. We will get back to you here as soon as we can.");

    /// <summary>The same, at once, while support is away.</summary>
    public static readonly TextKey NoAnswerAway = new("team.support.transcript.noAnswerAway",
        "Support is away right now. We will get back to you here as soon as we can.");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        Assistant, Support, NotSentYet, NotDelivered, SendAgain, Typing, Lookup, LookupOrdinals, LookupNumbered, ServiceCall, ServiceCalls,
        Helpful, NotHelpful, RatedHelpful, RatedNotHelpful, NoAnswerYet, NoAnswerAway
    ];
}
