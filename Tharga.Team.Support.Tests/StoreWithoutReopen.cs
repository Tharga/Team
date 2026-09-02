namespace Tharga.Team.Support.Tests;

/// <summary>
/// A store written before reopening existed: it implements every required member of
/// <see cref="ISupportCaseStore"/> and leaves <see cref="ISupportCaseStore.ReopenCaseAsync"/> to the
/// interface's own default.
/// </summary>
/// <remarks>
/// <b>This is the host that must not break.</b> The port is implementable by anyone choosing their own
/// storage, so a new required member would be a compile error in somebody else's repository. Declaring every
/// member here by hand is the point — it is exactly what a host has, and if a future member arrives without
/// a default, this file stops compiling and says so before a consumer finds out.
/// <para>
/// Delegates to an inner store so it behaves normally for everything it does implement, which keeps the
/// reopen assertion about the default member rather than about a stub that does nothing.
/// </para>
/// </remarks>
internal sealed class StoreWithoutReopen(InMemorySupportCaseStore inner) : ISupportCaseStore
{
    public Task<SupportCase> GetCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
        => inner.GetCaseAsync(teamKey, caseId, cancellationToken);

    public Task<SupportCasePage> GetCasesAsync(string teamKey, string cursor, int pageSize, CancellationToken cancellationToken = default)
        => inner.GetCasesAsync(teamKey, cursor, pageSize, cancellationToken);

    public Task<SupportCasePage> GetCasesByAuthorAsync(string teamKey, string authorIdentity, string cursor, int pageSize, CancellationToken cancellationToken = default)
        => inner.GetCasesByAuthorAsync(teamKey, authorIdentity, cursor, pageSize, cancellationToken);

    public Task<SupportMessagePage> GetMessagesAsync(string teamKey, string caseId, string cursor, int pageSize, CancellationToken cancellationToken = default)
        => inner.GetMessagesAsync(teamKey, caseId, cursor, pageSize, cancellationToken);

    public Task AddCaseAsync(SupportCase supportCase, SupportMessage firstMessage, CancellationToken cancellationToken = default)
        => inner.AddCaseAsync(supportCase, firstMessage, cancellationToken);

    public Task AppendMessageAsync(string teamKey, string caseId, SupportMessage message, CancellationToken cancellationToken = default)
        => inner.AppendMessageAsync(teamKey, caseId, message, cancellationToken);

    public Task CloseCaseAsync(string teamKey, string caseId, DateTime closedAt, string closedBy, SupportMessage closureMessage, CancellationToken cancellationToken = default)
        => inner.CloseCaseAsync(teamKey, caseId, closedAt, closedBy, closureMessage, cancellationToken);

    public Task<SupportCase> GetCaseByBindingAsync(SupportChannelType channelType, string externalId, CancellationToken cancellationToken = default)
        => inner.GetCaseByBindingAsync(channelType, externalId, cancellationToken);

    public Task MarkReadAsync(string teamKey, string caseId, string identity, int sequence, CancellationToken cancellationToken = default)
        => inner.MarkReadAsync(teamKey, caseId, identity, sequence, cancellationToken);

    public Task<int> GetUnreadCountAsync(string teamKey, string identity, CancellationToken cancellationToken = default)
        => inner.GetUnreadCountAsync(teamKey, identity, cancellationToken);

    public Task<int> GetAwaitingSupportCountAsync(string teamKey, CancellationToken cancellationToken = default)
        => inner.GetAwaitingSupportCountAsync(teamKey, cancellationToken);

    public Task AddBindingAsync(string teamKey, string caseId, SupportChannelBinding binding, CancellationToken cancellationToken = default)
        => inner.AddBindingAsync(teamKey, caseId, binding, cancellationToken);

    public Task SetMessageDeliveryAsync(string teamKey, string caseId, int sequence, SupportMessageDelivery delivery, CancellationToken cancellationToken = default)
        => inner.SetMessageDeliveryAsync(teamKey, caseId, sequence, delivery, cancellationToken);

    public Task<int> DeleteCasesForTeamAsync(string teamKey, CancellationToken cancellationToken = default)
        => inner.DeleteCasesForTeamAsync(teamKey, cancellationToken);
}
