namespace Tharga.Team;

/// <summary>What the person answering chose for one answer: the address it is sent from, and whether it is signed with their name.</summary>
public sealed record SupportAnswerChoice
{
    /// <summary>The address to send from, one of <see cref="SupportAnswerRoute.Senders"/>; null uses <see cref="SupportAnswerRoute.From"/>.</summary>
    public string SendFrom { get; init; }

    /// <summary>Whether the answer is signed with the answerer's name; null uses the host's default.</summary>
    public bool? SignWithName { get; init; }
}
