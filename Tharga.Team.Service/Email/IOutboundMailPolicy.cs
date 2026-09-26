namespace Tharga.Team.Service.Email;

/// <summary>
/// Decides, per mail, whether it may reach its recipient in the current environment (Tharga/Team#290).
/// </summary>
/// <remarks>
/// Every mail the toolkit sends goes through this: invitations and support mail alike. It is registered whatever
/// sender the host uses, so <b>a custom <c>ITeamEmailSender</c> can apply the same rules</b> by injecting it and
/// sending according to the returned <see cref="OutboundMailDecision"/>. The toolkit cannot apply it to a sender
/// it does not own.
/// </remarks>
public interface IOutboundMailPolicy
{
    /// <summary>The decision for one mail to <paramref name="recipient"/> with <paramref name="subject"/>.</summary>
    OutboundMailDecision Decide(string recipient, string subject);
}
