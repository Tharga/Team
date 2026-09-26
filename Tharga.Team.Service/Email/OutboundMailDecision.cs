namespace Tharga.Team.Service.Email;

/// <summary>What <see cref="IOutboundMailPolicy"/> decided for one mail.</summary>
public enum OutboundMailAction
{
    /// <summary>Send to the intended recipient, unchanged.</summary>
    Deliver,

    /// <summary>Send to the override address instead, with the subject naming the intended recipient.</summary>
    Redirect,

    /// <summary>Do not send.</summary>
    Withhold
}

/// <summary>
/// The outcome of <see cref="IOutboundMailPolicy.Decide"/>: send to <see cref="Recipient"/> with
/// <see cref="Subject"/>, unless <see cref="ShouldSend"/> is false.
/// </summary>
/// <param name="Action">What was decided.</param>
/// <param name="Recipient">The address to send to. The intended recipient when withheld, for logging.</param>
/// <param name="Subject">The subject to send with.</param>
public sealed record OutboundMailDecision(OutboundMailAction Action, string Recipient, string Subject)
{
    /// <summary>False when the mail must not be sent at all.</summary>
    public bool ShouldSend => Action != OutboundMailAction.Withhold;
}
