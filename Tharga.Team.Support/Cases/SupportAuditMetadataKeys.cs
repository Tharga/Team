namespace Tharga.Team.Support.Cases;

/// <summary>
/// Metadata keys support-case audit entries use, following the dotted convention of
/// <c>AuditMetadataKeys</c>.
/// </summary>
/// <remarks>
/// <b>There is deliberately no key for a message body.</b> A support case is where somebody pastes a
/// password, a token or a customer's details, and an audit entry travels further than the case does — more
/// readers, longer retention, easier export. Recording that a reply happened is the audit's job; recording
/// what it said is the case's.
/// </remarks>
public static class SupportAuditMetadataKeys
{
    public const string CaseId = "support.case.id";

    public const string CaseSubject = "support.case.subject";
}
