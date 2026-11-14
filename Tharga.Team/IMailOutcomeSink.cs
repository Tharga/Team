namespace Tharga.Team;

/// <summary>Told when a logged mail is given up or bounces, on the instance that observed it and once per transition; delivering it anywhere else, such as to other instances or to a screen, is the host's job.</summary>
/// <remarks>Every registered sink is told; one that throws is logged, the others are still told, and the mail is never affected.</remarks>
public interface IMailOutcomeSink
{
    /// <summary>Receives one outcome.</summary>
    Task OnMailOutcomeAsync(MailOutcome outcome, CancellationToken cancellationToken);
}
