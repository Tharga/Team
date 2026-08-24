namespace Tharga.Team;

/// <summary>
/// The persistence port for support cases — what the domain needs from a store, in the domain's language.
/// </summary>
/// <remarks>
/// <b>A port, not a repository.</b> Nothing here names or inherits a storage type: no <c>IRepository</c>
/// base, no driver types, no filter objects. <c>IApiKeyRepository : IRepository</c> is the shape this
/// deliberately avoids — a port defined in one store's terms means the second adapter has to implement the
/// first store's idea of persistence.
/// <para>
/// <b>Every method takes the team.</b> A case id alone never identifies a case, so the tenant boundary is
/// expressed by the port itself rather than trusted to each caller to remember. That is what makes the
/// cross-tenant read hard to write by accident.
/// </para>
/// <para>
/// <b>Two methods carry a message alongside a state change, and that is the atomicity contract.</b> Raising
/// a case creates a case <i>and</i> its first message; closing one sets a status <i>and</i> records why. An
/// adapter must apply each as a single unit, or a crash leaves a case with no transcript and the model's
/// central promise — a case always has at least one message — becomes untrue.
/// </para>
/// </remarks>
public interface ISupportCaseStore
{
    /// <summary>One case, or <c>null</c> if the team has no such case.</summary>
    Task<SupportCase> GetCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default);

    /// <summary>Every case in a team, newest first.</summary>
    Task<SupportCasePage> GetCasesAsync(string teamKey, string cursor, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Cases raised by one author, newest first.</summary>
    Task<SupportCasePage> GetCasesByAuthorAsync(string teamKey, string authorIdentity, string cursor, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>A case's transcript in order, oldest first.</summary>
    Task<SupportMessagePage> GetMessagesAsync(string teamKey, string caseId, string cursor, int pageSize, CancellationToken cancellationToken = default);

    /// <summary>Creates a case together with its first message, as one unit.</summary>
    Task AddCaseAsync(SupportCase supportCase, SupportMessage firstMessage, CancellationToken cancellationToken = default);

    /// <summary>Appends one entry to a case's transcript.</summary>
    Task AppendMessageAsync(string teamKey, string caseId, SupportMessage message, CancellationToken cancellationToken = default);

    /// <summary>Closes a case and records the closure in its transcript, as one unit.</summary>
    Task CloseCaseAsync(string teamKey, string caseId, DateTime closedAt, string closedBy, SupportMessage closureMessage, CancellationToken cancellationToken = default);

    /// <summary>
    /// The case projected onto a given channel identifier, or <c>null</c> if no case is bound to it.
    /// </summary>
    /// <remarks>
    /// <b>The only read here that is not scoped by team</b>, and it has to be: an inbound event arrives
    /// carrying a channel's identifier and nothing else, so the binding is what resolves the team rather
    /// than something the caller supplies. It is reached only from a channel adapter handling a verified
    /// event, never from a user-facing path.
    /// </remarks>
    Task<SupportCase> GetCaseByBindingAsync(SupportChannelType channelType, string externalId, CancellationToken cancellationToken = default);

    /// <summary>Records a case's projection onto an external channel.</summary>
    Task AddBindingAsync(string teamKey, string caseId, SupportChannelBinding binding, CancellationToken cancellationToken = default);

    /// <summary>
    /// Records whether one transcript entry reached the case's channel.
    /// </summary>
    /// <remarks>
    /// A second write after the message itself, deliberately. The case is written first and is authoritative;
    /// the channel is a projection, so a channel that is slow or down must not delay or block the record of
    /// what somebody said.
    /// </remarks>
    Task SetMessageDeliveryAsync(string teamKey, string caseId, int sequence, SupportMessageDelivery delivery, CancellationToken cancellationToken = default);

    /// <summary>
    /// Destroys every case belonging to a team. Backs the purge cascade.
    /// </summary>
    /// <remarks>
    /// Purging a team drops the host's per-team database, which does not reach the toolkit's own shared
    /// collections — so without this the cases would outlive the team that owned them.
    /// </remarks>
    Task<int> DeleteCasesForTeamAsync(string teamKey, CancellationToken cancellationToken = default);
}
