using Microsoft.Extensions.Logging;

namespace Tharga.Team;

/// <summary>Tells every registered <see cref="IMailOutcomeSink"/> about a mail that ended badly; one that throws is logged and the rest still run.</summary>
public sealed class MailOutcomeNotifier(IEnumerable<IMailOutcomeSink> sinks = null, ILogger<MailOutcomeNotifier> logger = null)
{
    private readonly IMailOutcomeSink[] _sinks = sinks?.ToArray() ?? [];

    public async Task NotifyAsync(MailOutcome outcome, CancellationToken cancellationToken = default)
    {
        foreach (var sink in _sinks)
        {
            try
            {
                await sink.OnMailOutcomeAsync(outcome, cancellationToken);
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // The reason is left out of the log: it often quotes the recipient's address.
                logger?.LogWarning(e, "A mail outcome sink {Sink} failed for mail {MailId} ({Outcome}); the others were still told.", sink.GetType().Name, outcome.MailId, outcome.Outcome);
            }
        }
    }
}
