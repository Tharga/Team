using System.Diagnostics;
using Tharga.Team.Service.Audit;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Records a case opened from a public form in the audit trail, as <c>support:raise</c> with a source that says it came
/// from a web form.
/// </summary>
/// <remarks>
/// <b>The same action as a case raised on the site</b>, so a route that tells support a case arrived tells them about
/// this one too; what tells them apart is <see cref="SupportAuditMetadataKeys.Source"/>. As there, the message body and
/// the visitor's details are never recorded: an entry travels further than the case does.
/// <para>
/// <b>A refusal is recorded as a failed entry</b>, so a form being fed malformed addresses shows in the trail.
/// </para>
/// </remarks>
internal sealed class AuditingExternalSupportCaseServiceDecorator(
    IExternalSupportCaseService inner,
    CompositeAuditLogger auditLogger,
    IAuditEntryFactory auditEntryFactory) : IExternalSupportCaseService
{
    private const string Feature = "support";

    public async Task<SupportCase> RaiseExternalCaseAsync(ExternalSupportContact contact, string subject, string body, CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            var raised = await inner.RaiseExternalCaseAsync(contact, subject, body, cancellationToken);

            Log(sw.ElapsedMilliseconds, true, null, new Dictionary<string, string>
            {
                [SupportAuditMetadataKeys.CaseId] = raised.Id,
                [SupportAuditMetadataKeys.CaseSubject] = raised.Subject,
                [SupportAuditMetadataKeys.WithAssistant] = "false",
                [SupportAuditMetadataKeys.Source] = SupportAuditMetadataKeys.WebFormSource
            });

            return raised;
        }
        catch (Exception ex)
        {
            Log(sw.ElapsedMilliseconds, false, ex.Message, new Dictionary<string, string>
            {
                [SupportAuditMetadataKeys.Source] = SupportAuditMetadataKeys.WebFormSource
            });
            throw;
        }
    }

    private void Log(long durationMs, bool success, string errorMessage, IReadOnlyDictionary<string, string> metadata)
        => auditLogger.Log(auditEntryFactory.Create(Feature, "raise", nameof(RaiseExternalCaseAsync), durationMs, success, errorMessage, null, metadata));
}
