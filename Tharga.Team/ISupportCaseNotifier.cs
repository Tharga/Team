namespace Tharga.Team;

/// <summary>
/// Tells the host when a support case changed, whichever side changed it.
/// </summary>
/// <remarks>
/// <b>A singleton, and it has to be one.</b> A reply arriving from Slack is handled in the inbound
/// endpoint's own request scope; a page waiting to render it lives in a Blazor circuit's scope. An event
/// raised on a scoped service reaches only the instance that raised it, so the two would never meet. Putting
/// the event on <c>ISupportCaseService</c> looks natural and silently does nothing across scopes.
/// <para>
/// <b>Handlers run on whichever thread raised the event</b> — for an inbound reply that is a request thread
/// handling a Slack POST, not a Blazor circuit. A component must marshal with <c>InvokeAsync</c> before
/// touching its own state, or it will update off the circuit's synchronization context and either lose the
/// render or throw.
/// </para>
/// <para>
/// <b>Unsubscribe.</b> This outlives every page that listens to it, so a component that subscribes and never
/// detaches keeps itself alive for the lifetime of the application. Subscribe in <c>OnInitialized</c>,
/// unsubscribe in <c>Dispose</c>.
/// </para>
/// <para>
/// <b>It carries no authorization.</b> Every subscriber is told about every case in every team, because
/// filtering here would need a caller and a singleton has none. A handler that acts on the notification must
/// read the case back through <see cref="ISupportCaseService"/>, where the checks are.
/// </para>
/// </remarks>
public interface ISupportCaseNotifier
{
    /// <summary>Raised after a case is created, replied to or closed.</summary>
    event EventHandler<SupportCaseUpdatedEventArgs> CaseUpdated;

    /// <summary>Announces a change. Called by the toolkit; a host subscribes rather than raises.</summary>
    void Notify(SupportCaseUpdatedEventArgs args);
}
