using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Features.Support;

/// <summary>
/// Tells the other side of a case that this reader is writing, at most once every <see cref="Interval"/>.
/// </summary>
/// <remarks>
/// Reports go one after another and never hold up the keystroke that caused them. A report that fails is dropped:
/// the dots are a courtesy, and the message itself goes through the service's own checks.
/// </remarks>
internal sealed class SupportTypingReporter(ISupportCaseService cases, TimeProvider time = null)
{
    /// <summary>How often a writer says they are still writing.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private string _caseId;
    private DateTimeOffset? _reportedAt;
    private Task _pending = Task.CompletedTask;

    /// <summary>Called on every keystroke in an answer to an existing case.</summary>
    public void Typed(string teamKey, string caseId, bool asSupport)
    {
        if (caseId == null) return;

        var now = _time.GetUtcNow();
        if (_caseId == caseId && _reportedAt != null && now - _reportedAt < Interval) return;

        _caseId = caseId;
        _reportedAt = now;
        _pending = ReportAsync(_pending, teamKey, caseId, asSupport);
    }

    /// <summary>
    /// Waits for a report on its way, so none lands after the message it announced, and starts afresh: the next
    /// keystroke reports at once.
    /// </summary>
    public async Task SendingAsync()
    {
        await _pending;
        _reportedAt = null;
    }

    private async Task ReportAsync(Task previous, string teamKey, string caseId, bool asSupport)
    {
        await previous;

        try
        {
            await cases.ReportTypingAsync(teamKey, caseId, asSupport);
        }
        catch (Exception)
        {
        }
    }
}
