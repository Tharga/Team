namespace Tharga.Team.Support.Cases;

/// <summary>
/// Support-case operations. The surface a host, a component or a channel adapter calls.
/// </summary>
/// <remarks>
/// <b>Operations, not CRUD.</b> There is no <c>UpdateCase</c>: raising, replying and closing are three
/// distinct facts, each separately authorizable and separately auditable. A general update would be none of
/// those, because its legitimacy would depend on which of the three it was really doing.
/// <para>
/// <b>Every method names its team.</b> A case id alone never identifies a case — the store requires the team
/// too — so a caller cannot reach another tenant's case by holding an id, and the authorization decorator
/// has a team to check against on every call.
/// </para>
/// <para>
/// Authorization is applied by the decorator over this interface and nowhere else. Implementations of this
/// interface enforce nothing, and a component that renders a button still has its call checked.
/// </para>
/// </remarks>
public interface ISupportCaseService
{
    /// <summary>Raises a case for a team, with its opening message.</summary>
    Task<SupportCase> RaiseCaseAsync(string teamKey, string subject, string body, CancellationToken cancellationToken = default);

    /// <summary>Appends a reply to an open case.</summary>
    Task ReplyToCaseAsync(string teamKey, string caseId, string body, CancellationToken cancellationToken = default);

    /// <summary>Closes a case and records who closed it in its transcript.</summary>
    Task CloseCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default);

    /// <summary>One case, or <c>null</c> when the team has no such case.</summary>
    Task<SupportCase> GetCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default);

    /// <summary>Every case in a team, newest first.</summary>
    Task<SupportCasePage> GetCasesAsync(string teamKey, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default);

    /// <summary>The caller's own cases in a team, newest first.</summary>
    Task<SupportCasePage> GetMyCasesAsync(string teamKey, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default);

    /// <summary>A case's transcript, oldest first.</summary>
    Task<SupportMessagePage> GetMessagesAsync(string teamKey, string caseId, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default);
}
