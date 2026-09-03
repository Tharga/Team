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

    /// <summary>
    /// Opens a closed case again, keeping its history.
    /// </summary>
    /// <remarks>
    /// <b>Authorized exactly as replying is</b> — the member who raised the case, or a caller holding
    /// <c>support:read</c> or <c>support:manage</c> on the team. Somebody who could not answer a case has no
    /// business changing its state.
    /// <para>
    /// <b>This is what makes closing safe to do.</b> Without it, closing is a decision somebody has to be
    /// sure about, and the safe move is to leave cases open forever — which is how a case list stops being
    /// read. A case that closed too early costs one click to bring back, and it brings the conversation with
    /// it rather than starting a second case that explains nothing.
    /// </para>
    /// <para>
    /// Reopening an already-open case does nothing and is not an error.
    /// </para>
    /// </remarks>
    Task ReopenCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gives an unassigned case to a team.
    /// </summary>
    /// <remarks>
    /// <b>The only way a case gains a team after the fact</b>, and an operation rather than a property set
    /// because it is one authorizable, auditable fact: it decides which tenant a case and its entire
    /// transcript become part of.
    /// <para>
    /// <b>Authorized by <see cref="SystemSupportScopes.Manage"/></b>, not by a scope on the receiving team.
    /// A member of one team must not be able to pull a case that may concern another into their own.
    /// </para>
    /// <para>
    /// <b>Only an unassigned case can be assigned.</b> Moving a case between tenants moves its transcript
    /// with it, so it is refused rather than treated as a correction — if it is genuinely wrong, that is a
    /// decision somebody should have to make deliberately, and there is no evidence yet that anybody needs
    /// to.
    /// </para>
    /// </remarks>
    Task<bool> AssignCaseAsync(string caseId, string teamKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cases belonging to no team, for whoever triages them.
    /// </summary>
    /// <remarks>
    /// Authorized by <see cref="SystemSupportScopes.Read"/>. Separate from
    /// <see cref="GetCasesAsync"/> because that one answers "what is happening in this team" and this one
    /// answers "what has arrived that nobody owns" — different questions, different grants.
    /// </remarks>
    Task<SupportCasePage> GetUnassignedCasesAsync(string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default);

    /// <summary>One case, or <c>null</c> when the team has no such case.</summary>
    Task<SupportCase> GetCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default);

    /// <summary>Every case in a team, newest first.</summary>
    Task<SupportCasePage> GetCasesAsync(string teamKey, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default);

    /// <summary>The caller's own cases in a team, newest first.</summary>
    Task<SupportCasePage> GetMyCasesAsync(string teamKey, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records that the caller has read this case up to its newest entry.
    /// </summary>
    /// <remarks>
    /// Authorized exactly as reading the case is — anything weaker would let somebody write to a case they
    /// cannot see.
    /// </remarks>
    Task MarkReadAsync(string teamKey, string caseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many of the caller's own cases hold entries they have not read. What a per-user indicator shows.
    /// </summary>
    Task<int> GetMyUnreadCountAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many open cases in the team are waiting on an answer — their newest entry came from the person
    /// who raised them. What a support-side indicator shows.
    /// </summary>
    /// <remarks>
    /// Counts across everybody's cases, so it is exactly as privileged as reading them and requires
    /// <c>support:read</c>.
    /// </remarks>
    Task<int> GetAwaitingSupportCountAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>A case's transcript, oldest first.</summary>
    Task<SupportMessagePage> GetMessagesAsync(string teamKey, string caseId, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default);
}
