using System.Diagnostics;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Records raising, replying to and closing a support case in the audit trail.
/// </summary>
/// <remarks>
/// <b>Three distinct facts, not one "case changed" event.</b> That is the audit half of the same choice the
/// service surface makes: operations rather than CRUD. A single update entry would say a case moved without
/// saying whether somebody answered a customer or ended the conversation.
/// <para>
/// <b>Reads are not audited</b>, consistent with team enumeration and the directory-only listing. The
/// exception worth noting is that reading <i>somebody else's</i> case is a privileged act — if that ever
/// needs a trail, it belongs here, and it is a deliberate decision rather than an oversight that it has none
/// today.
/// </para>
/// <para>
/// <b>Message bodies are never recorded.</b> A support case is exactly where somebody pastes a password or a
/// customer's details, and an audit entry is read by more people, kept longer and exported more freely than
/// the case itself. The metadata carries the case id and the subject only.
/// </para>
/// <para>
/// <b>Wraps the authorizing decorator, so a refusal is recorded rather than lost.</b> The entry is written
/// with <c>Success = false</c> and the reason, which is the same choice the toolkit already makes for
/// access-level and scope denials — a denied attempt to read somebody else's support case is more worth
/// knowing about than a permitted one, not less. Composing these the other way round would silently drop
/// every refusal, and nothing would fail to compile.
/// </para>
/// </remarks>
internal sealed class AuditingSupportCaseServiceDecorator(
    ISupportCaseService inner,
    CompositeAuditLogger auditLogger,
    IAuditEntryFactory auditEntryFactory) : ISupportCaseService
{
    private const string Feature = "support";

    public async Task<SupportCase> RaiseCaseAsync(string teamKey, string subject, string body, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var raised = await inner.RaiseCaseAsync(teamKey, subject, body, cancellationToken);

            // raised.Subject, not the argument: with UseSubject off the caller supplies none and the service
            // derives one, so recording the argument records nothing -- and a notification worded around
            // {support.case.subject} then names the case with an empty string.
            Log("raise", nameof(RaiseCaseAsync), sw.ElapsedMilliseconds, true, teamKey,
                metadata: Meta((SupportAuditMetadataKeys.CaseId, raised.Id), (SupportAuditMetadataKeys.CaseSubject, raised.Subject)));

            return raised;
        }
        catch (Exception ex)
        {
            Log("raise", nameof(RaiseCaseAsync), sw.ElapsedMilliseconds, false, teamKey, ex.Message,
                Meta((SupportAuditMetadataKeys.CaseSubject, subject)));
            throw;
        }
    }

    public async Task ReplyToCaseAsync(string teamKey, string caseId, string body, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await inner.ReplyToCaseAsync(teamKey, caseId, body, cancellationToken);

            Log("reply", nameof(ReplyToCaseAsync), sw.ElapsedMilliseconds, true, teamKey,
                metadata: Meta((SupportAuditMetadataKeys.CaseId, caseId)));
        }
        catch (Exception ex)
        {
            Log("reply", nameof(ReplyToCaseAsync), sw.ElapsedMilliseconds, false, teamKey, ex.Message,
                Meta((SupportAuditMetadataKeys.CaseId, caseId)));
            throw;
        }
    }

    public async Task CloseCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await inner.CloseCaseAsync(teamKey, caseId, cancellationToken);

            Log("close", nameof(CloseCaseAsync), sw.ElapsedMilliseconds, true, teamKey,
                metadata: Meta((SupportAuditMetadataKeys.CaseId, caseId)));
        }
        catch (Exception ex)
        {
            Log("close", nameof(CloseCaseAsync), sw.ElapsedMilliseconds, false, teamKey, ex.Message,
                Meta((SupportAuditMetadataKeys.CaseId, caseId)));
            throw;
        }
    }

    public async Task ReopenCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            await inner.ReopenCaseAsync(teamKey, caseId, cancellationToken);

            Log("reopen", nameof(ReopenCaseAsync), sw.ElapsedMilliseconds, true, teamKey,
                metadata: Meta((SupportAuditMetadataKeys.CaseId, caseId)));
        }
        catch (Exception ex)
        {
            Log("reopen", nameof(ReopenCaseAsync), sw.ElapsedMilliseconds, false, teamKey, ex.Message,
                Meta((SupportAuditMetadataKeys.CaseId, caseId)));
            throw;
        }
    }

    public Task<SupportCase> GetCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
        => inner.GetCaseAsync(teamKey, caseId, cancellationToken);

    // Reads and counters, not audited - consistent with every other read. Marking a case read is a write,
    // but it records that somebody looked at something rather than that anything changed, and auditing it
    // would fill the log with one entry per page view.
    public Task MarkReadAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
        => inner.MarkReadAsync(teamKey, caseId, cancellationToken);

    public Task<int> GetMyUnreadCountAsync(string teamKey, CancellationToken cancellationToken = default)
        => inner.GetMyUnreadCountAsync(teamKey, cancellationToken);

    public Task<int> GetAwaitingSupportCountAsync(string teamKey, CancellationToken cancellationToken = default)
        => inner.GetAwaitingSupportCountAsync(teamKey, cancellationToken);

    public Task<SupportCasePage> GetCasesAsync(string teamKey, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default)
        => inner.GetCasesAsync(teamKey, cursor, pageSize, cancellationToken);

    public Task<SupportCasePage> GetMyCasesAsync(string teamKey, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default)
        => inner.GetMyCasesAsync(teamKey, cursor, pageSize, cancellationToken);

    public Task<SupportMessagePage> GetMessagesAsync(string teamKey, string caseId, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default)
        => inner.GetMessagesAsync(teamKey, caseId, cursor, pageSize, cancellationToken);

    /// <remarks>
    /// Built through <see cref="IAuditEntryFactory"/> rather than by hand, because a hand-constructed
    /// <c>AuditEntry</c> is never given the ambient actor — so an entry written from background work would
    /// be attributed to nobody. Logged through <c>CompositeAuditLogger</c> rather than a single
    /// <c>IAuditLogger</c>, so the caller and event filters apply and every configured sink sees it.
    /// </remarks>
    private void Log(string action, string methodName, long durationMs, bool success, string teamKey, string errorMessage = null, IReadOnlyDictionary<string, string> metadata = null)
    {
        var entry = auditEntryFactory.Create(Feature, action, methodName, durationMs, success, errorMessage, teamKey, metadata);

        auditLogger.Log(entry);
    }

    private static Dictionary<string, string> Meta(params (string Key, string Value)[] pairs)
    {
        var metadata = new Dictionary<string, string>();

        foreach (var (key, value) in pairs)
        {
            if (value != null) metadata[key] = value;
        }

        return metadata;
    }
}
