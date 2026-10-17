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

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Assistant, Support, NotSentYet, NotDelivered, SendAgain];
}
