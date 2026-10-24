namespace Tharga.Team;

/// <summary>
/// Tells the host when somebody is writing in a support case, so the other side can show that an answer is on its way.
/// </summary>
/// <remarks>
/// <b>Apart from <see cref="ISupportCaseNotifier"/>, because nothing is stored.</b> Every listener to a case change
/// reads the case again; a signal several times a minute per writer would turn each into a query loop for a
/// change that never happened.
/// <para>
/// A singleton raised on whichever thread reported it, carrying no authorization, exactly as
/// <see cref="ISupportCaseNotifier"/> is: marshal with <c>InvokeAsync</c>, and unsubscribe in <c>Dispose</c>.
/// Reporting goes through <see cref="Support.Cases.ISupportCaseService.ReportTypingAsync"/>, where only somebody
/// who may write in the case is let through.
/// </para>
/// </remarks>
public interface ISupportTypingNotifier
{
    /// <summary>Raised each time somebody reports writing in a case.</summary>
    event EventHandler<SupportCaseTypingEventArgs> Typing;

    /// <summary>Announces it. Called by the toolkit; a host subscribes rather than raises.</summary>
    void Notify(SupportCaseTypingEventArgs args);
}
