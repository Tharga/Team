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
    /// <remarks>
    /// <paramref name="assistance"/> is the customer's choice of who answers, and it is the only place that
    /// choice is made. Asking for an assistant when the host has registered none is not an error: the case is
    /// raised and a person answers it, which is what the customer would have got anyway.
    /// </remarks>
    Task<SupportCase> RaiseCaseAsync(string teamKey, string subject, string body, SupportAssistance assistance = SupportAssistance.None, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops the assistant answering a case, so a person takes over.
    /// </summary>
    /// <remarks>
    /// <b>Same case, same transcript.</b> Handing over is not a second case: whoever picks it up reads
    /// everything that was already said, which is the whole point of offering an assistant first.
    /// <para>
    /// Authorized exactly as replying is. On a case with no assistant this does nothing and is not an error,
    /// so a component can offer it without first working out whether it applies.
    /// </para>
    /// </remarks>
    Task RequestHumanAsync(string teamKey, string caseId, CancellationToken cancellationToken = default);

    /// <summary>Appends a reply to an open case.</summary>
    Task ReplyToCaseAsync(string teamKey, string caseId, string body, CancellationToken cancellationToken = default);

    /// <summary>Appends a reply carrying files to an open case, authorized exactly as a reply without files.</summary>
    Task ReplyToCaseAsync(string teamKey, string caseId, string body, IReadOnlyList<SupportAttachmentUpload> attachments, CancellationToken cancellationToken = default);

    /// <summary>Appends a formatted reply with any files, authorized as a plain reply: <paramref name="bodyHtml"/> is sanitized, its images become their alt text, the text is derived from it, and <paramref name="body"/> is used only when it holds nothing readable.</summary>
    Task ReplyToCaseAsync(string teamKey, string caseId, string body, string bodyHtml, IReadOnlyList<SupportAttachmentUpload> attachments, CancellationToken cancellationToken = default);

    /// <summary>
    /// Answers a case from the support side. The answer counts as support's whoever writes it, so it takes the
    /// case out of the waiting count and is unread for the person who raised it until they open it.
    /// </summary>
    /// <remarks>
    /// Needs a grant to answer on behalf of support — <see cref="SystemSupportScopes.AllManage"/>,
    /// <c>support:manage</c> on the case's team, or for a case with no team
    /// <see cref="SystemSupportScopes.Manage"/>. Having raised the case confers nothing here.
    /// </remarks>
    Task AnswerCaseAsync(string teamKey, string caseId, string body, CancellationToken cancellationToken = default);

    /// <summary>Answers a case from the support side with files, authorized exactly as an answer without files.</summary>
    Task AnswerCaseAsync(string teamKey, string caseId, string body, IReadOnlyList<SupportAttachmentUpload> attachments, CancellationToken cancellationToken = default);

    /// <summary>Answers a case from the support side with formatted text and any files, authorized as a plain answer and sanitized and derived as the formatted reply is.</summary>
    Task AnswerCaseAsync(string teamKey, string caseId, string body, string bodyHtml, IReadOnlyList<SupportAttachmentUpload> attachments, CancellationToken cancellationToken = default);

    /// <summary>Opens a file carried by one entry of a case, authorized exactly as reading its transcript, or returns null when that entry carries no attachment with this id; the caller disposes it and serves it as <see cref="SupportAttachmentContentTypes.ServedAs"/> says.</summary>
    Task<SupportAttachmentContent> OpenAttachmentAsync(string teamKey, string caseId, int sequence, string attachmentId, CancellationToken cancellationToken = default);

    /// <summary>How many files, and how large, a reply or answer may carry, so a component offers what the service accepts.</summary>
    Task<SupportAttachmentLimits> GetAttachmentLimitsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Sends one transcript entry to the case's channels again, and returns where it stands now.
    /// </summary>
    /// <remarks>
    /// Only an entry that is <see cref="SupportMessageDelivery.Failed"/> or <see cref="SupportMessageDelivery.Pending"/>
    /// is sent; any other is left alone and its status returned, so pressing the button again after it went
    /// through sends nothing twice. Authorized exactly as <see cref="AnswerCaseAsync(string, string, string, CancellationToken)"/> is.
    /// </remarks>
    Task<SupportMessageDelivery> ResendAsync(string teamKey, string caseId, int sequence, CancellationToken cancellationToken = default);

    /// <summary>
    /// Where support's answer to the case goes — the address it is mailed to and the mailbox it is sent from, and
    /// what this environment's outbound mail policy does with it — or that it is shown on the site; null when the
    /// team has no such case.
    /// </summary>
    /// <remarks>Authorized exactly as <see cref="AnswerCaseAsync(string, string, string, CancellationToken)"/> is: the address is shown to whoever may answer.</remarks>
    Task<SupportAnswerRoute> GetAnswerRouteAsync(string teamKey, string caseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// A draft answer as the person who raised the case would receive it by mail, in the host's template and with the
    /// conversation so far quoted, rendered without sending or storing anything; null when the answer would not be
    /// mailed or the team has no such case.
    /// </summary>
    /// <remarks>Authorized exactly as answering; the draft is sanitized and derived as an answer is. Files are not rendered.</remarks>
    Task<MailPreview> PreviewAnswerAsync(string teamKey, string caseId, string body, string bodyHtml, CancellationToken cancellationToken = default);

    /// <summary>
    /// Has the assistant answer the newest message on a case, and appends what it says.
    /// </summary>
    /// <remarks>
    /// <b>A separate operation rather than something raising and replying do for you.</b> A model can take
    /// tens of seconds, and burying that inside a write would make reporting a problem feel broken. Raising
    /// stays fast, and the caller decides when to ask for the answer — which also lets a UI say that the
    /// assistant is thinking.
    /// <para>
    /// Does nothing and returns <c>false</c> when the case has no assistant, when the customer has already
    /// asked for a person, when the host registered no responder, or when the caller is not the person who
    /// raised the case: the assistant's tools read with the caller's rights, so it answers only its author.
    /// Every one of those is an ordinary state rather than an error, so a caller can call this unconditionally.
    /// </para>
    /// </remarks>
    /// <returns>True when an answer was appended.</returns>
    Task<bool> RunAssistantAsync(string teamKey, string caseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs the assistant as <see cref="RunAssistantAsync(string, string, CancellationToken)"/> does, reporting to
    /// <paramref name="progress"/> how far it has got: the round, the tools running, and the answer as written so far.
    /// </summary>
    /// <remarks>
    /// <b>Only the caller sees the answer before it is kept.</b> Reports go to <paramref name="progress"/> and
    /// nowhere else, never through <see cref="ISupportCaseNotifier"/>, which carries no text and no authorization.
    /// Everybody else reads the answer once it is appended. Each report is the whole state, the text so far included;
    /// a responder that cannot stream reports nothing and answers in one piece. A report may arrive on another thread,
    /// so marshal before touching component state.
    /// </remarks>
    /// <returns>True when an answer was appended. When false, whatever was reported was not kept.</returns>
    Task<bool> RunAssistantAsync(string teamKey, string caseId, IProgress<SupportAssistantProgress> progress, CancellationToken cancellationToken = default);

    /// <summary>
    /// Whether the assistant can answer for this team, and if not, why.
    /// </summary>
    /// <remarks>
    /// <b>Ask this before offering the assistant.</b> A deployment can give each team its own model or none, so
    /// whether there is an assistant is a question about a team, not about the host. Raising a case that asks
    /// for the assistant applies the same answer: a team that is not ready gets a person.
    /// <para>
    /// Authorized by membership, as raising a case is. A responder that cannot tell, because its settings could
    /// not be read, reads as <see cref="SupportAssistantReadiness.NotConfigured"/> rather than an error.
    /// </para>
    /// </remarks>
    Task<SupportAssistantReadiness> GetAssistantReadinessAsync(string teamKey, CancellationToken cancellationToken = default);

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

    /// <summary>
    /// Cases across every team, for the product's own support staff.
    /// </summary>
    /// <remarks>
    /// Authorized by <see cref="SystemSupportScopes.AllRead"/>. The companion to that grant: without a
    /// listing, a holder can answer a case only once somebody hands them its id, which is not a queue.
    /// <para>
    /// <b>Does not include the unassigned queue</b> — that is <see cref="GetUnassignedCasesAsync"/> and
    /// <see cref="SystemSupportScopes.Read"/>. Holding one must never confer the other.
    /// </para>
    /// </remarks>
    Task<SupportCasePage> GetCasesAcrossTeamsAsync(string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default);

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
    /// Rates one of the assistant's answers in this case, or takes a rating back with <see cref="SupportAnswerRating.None"/>.
    /// </summary>
    /// <remarks>
    /// Only the person who raised the case rates, because it is them the assistant answered; anybody else is refused.
    /// Only an answer by the assistant can be rated. The case counts its ratings, for the people who build the assistant.
    /// </remarks>
    Task RateAnswerAsync(string teamKey, string caseId, int sequence, SupportAnswerRating rating, CancellationToken cancellationToken = default);

    /// <summary>
    /// Says the caller is writing in this case right now, so whoever is reading it on the other side sees an answer coming.
    /// </summary>
    /// <remarks>
    /// Raised through <see cref="ISupportTypingNotifier"/> and stored nowhere, so it is neither audited nor counted as
    /// activity. Authorized exactly as writing is: replying for a member, answering when
    /// <paramref name="asSupport"/>. Call it again every few seconds while writing; the signal lapses when no further
    /// one follows.
    /// </remarks>
    Task ReportTypingAsync(string teamKey, string caseId, bool asSupport, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sets whether the caller, on the support desk, is shown to members as there to answer.
    /// </summary>
    /// <remarks>
    /// For the product's support staff: a caller who may answer across teams or the queue of cases with no team; anybody
    /// else is refused. Setting it counts as acting in support. Kept in-process and stored nowhere, so it is not audited,
    /// and it is forgotten when the application restarts. Members see the outcome through <see cref="ISupportPresence"/>.
    /// </remarks>
    Task SetMyStaffStatusAsync(SupportStaffStatus status, CancellationToken cancellationToken = default);

    /// <summary>The caller's status on the support desk, or <c>null</c> when they have set none since the application started.</summary>
    /// <remarks>Authorized as <see cref="SetMyStaffStatusAsync"/> is.</remarks>
    Task<SupportStaffStatus?> GetMyStaffStatusAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Says the caller is at work on support right now, so their <see cref="SupportStaffStatus.Online"/> keeps counting.
    /// </summary>
    /// <remarks>
    /// Authorized as <see cref="SetMyStaffStatusAsync"/> is. Answering a case and writing as support count as well, without
    /// calling this. Online counts as away once the caller has done nothing in support for the host's idle time, ten
    /// minutes by default, and counts again with the next thing they do.
    /// </remarks>
    Task ReportStaffActivityAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// How many of the caller's own cases hold entries they have not read. What a per-user indicator shows.
    /// </summary>
    Task<int> GetMyUnreadCountAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// The caller's own cases that hold entries they have not read, each with how many — so a list can mark
    /// where to look rather than only that something is waiting.
    /// </summary>
    Task<SupportCaseUnread[]> GetMyUnreadByCaseAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many of the caller's own open cases support answered last — read or not, they wait for the caller
    /// to reply or to close them.
    /// </summary>
    Task<int> GetMyAnsweredCountAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>How many of the caller's own cases are open, conversations with the assistant aside.</summary>
    Task<int> GetMyOpenCountAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// What the caller may choose about who reads the case — so a component offers exactly what
    /// <see cref="SetCaseVisibilityAsync"/> would accept, without knowing who is asking.
    /// </summary>
    Task<SupportCaseVisibilityChoice> GetVisibilityChoiceAsync(string teamKey, string caseId, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many open cases in the team are waiting on an answer — their newest entry came from the person
    /// who raised them. What a support-side indicator shows.
    /// </summary>
    /// <remarks>
    /// Counts across everybody's cases, so it is exactly as privileged as reading them and requires
    /// <c>support:read</c>.
    /// </remarks>
    Task<int> GetAwaitingSupportCountAsync(string teamKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// How many open cases, in every team, are waiting on an answer. What a support-staff indicator shows.
    /// </summary>
    /// <remarks>
    /// Authorized by <see cref="SystemSupportScopes.AllRead"/> or <see cref="SystemSupportScopes.AllManage"/>,
    /// the grant that lists the same cases. Leaves out the unassigned queue, which
    /// <see cref="GetAwaitingUnassignedCountAsync"/> counts under its own grant.
    /// </remarks>
    Task<int> GetAwaitingSupportCountAcrossTeamsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// How many open cases that belong to no team are waiting on an answer.
    /// </summary>
    /// <remarks>
    /// Authorized by <see cref="SystemSupportScopes.Read"/> or <see cref="SystemSupportScopes.Manage"/>; the
    /// cross-team grant confers nothing here, as it confers nothing over the unassigned queue itself.
    /// </remarks>
    Task<int> GetAwaitingUnassignedCountAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Shares a case with the team's support readers, or takes it back.
    /// </summary>
    /// <remarks>
    /// Only the case's author, only while it is open, and only when the host enables per-case visibility —
    /// otherwise an <see cref="InvalidOperationException"/>. Nobody else may decide who reads somebody's
    /// conversation, the product's own support staff included.
    /// </remarks>
    Task SetCaseVisibilityAsync(string teamKey, string caseId, SupportCaseVisibility visibility, CancellationToken cancellationToken = default);

    /// <summary>A case's transcript, oldest first.</summary>
    Task<SupportMessagePage> GetMessagesAsync(string teamKey, string caseId, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default);
}
