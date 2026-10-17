namespace Tharga.Team;

/// <summary>
/// One received mail, as a team's mailbox or the postmaster holds it.
/// </summary>
public record InboundMailItem
{
    public required string Id { get; init; }

    /// <summary>The team the mail was routed to; null while the postmaster holds it.</summary>
    public string TeamKey { get; init; }

    /// <summary>
    /// The part after <c>+</c> in the address the mail was delivered to — an item inside the team, in the host's
    /// terms; null when there was none.
    /// </summary>
    public string ItemKey { get; init; }

    public required MailEnvelope Envelope { get; init; }

    public required MailRouteReason Reason { get; init; }

    public required DateTime ReceivedAt { get; init; }

    /// <summary>When the key a <see cref="MailRouteReason.RetiringKey"/> mail came to retires, so the team can tell the sender in time.</summary>
    public DateTime? KeyRetiresAt { get; init; }

    /// <summary>What the mail belongs to, once known.</summary>
    public MailThreadOwner Owner { get; init; }

    /// <summary>What became of the mail, in the host's terms; null while nobody has handled it.</summary>
    public string Outcome { get; init; }

    public DateTime? HandledAt { get; init; }

    public string HandledBy { get; init; }
}
