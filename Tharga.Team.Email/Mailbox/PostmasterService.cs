using Tharga.Team.Service;

namespace Tharga.Team.Email.Mailbox;

/// <summary>
/// The postmaster. Enforces nothing: it is registered behind the scope proxy as a system service, which checks
/// every call against the system scope its interface method declares and records each open.
/// </summary>
internal sealed class PostmasterService : IPostmasterService
{
    private readonly ITeamMailStore _mail;
    private readonly ITeamMailNotifier _notifier;
    private readonly IInboundMailContentStore _content;
    private readonly ITeamService _teams;
    private readonly ITeamPrincipalAccessor _principal;
    private readonly TimeProvider _time;

    public PostmasterService(ITeamMailStore mail, ITeamMailNotifier notifier, IInboundMailContentStore content = null, ITeamService teams = null, ITeamPrincipalAccessor principal = null, TimeProvider time = null)
    {
        _mail = mail;
        _notifier = notifier;
        _content = content;
        _teams = teams;
        _principal = principal;
        _time = time ?? TimeProvider.System;
    }

    public Task<PostmasterPage> ListAsync(string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default)
        => ListAsync(PostmasterMailState.Held, cursor, pageSize, cancellationToken);

    public async Task<PostmasterPage> ListAsync(PostmasterMailState state, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var page = await _mail.ListUnroutedAsync(state, cursor, pageSize, cancellationToken);

        return new PostmasterPage
        {
            Items = [.. page.Items.Select(Summary)],
            NextCursor = page.NextCursor
        };
    }

    public Task<int> GetCountAsync(CancellationToken cancellationToken = default) => _mail.CountUnroutedAsync(cancellationToken);

    public Task<InboundMailItem> OpenAsync(string mailId, CancellationToken cancellationToken = default) => _mail.GetUnroutedAsync(mailId, cancellationToken);

    public async Task<MailContent> GetContentAsync(string mailId, CancellationToken cancellationToken = default)
    {
        if (await _mail.GetUnroutedAsync(mailId, cancellationToken) == null) return null;

        return await TeamMailboxService.ContentOfAsync(_content, mailId, cancellationToken);
    }

    /// <remarks>
    /// The team is checked to exist when the host has a team service: routing a mail that may hold one
    /// organisation's material to a key that names nobody would put it where no one can ever read it.
    /// </remarks>
    public async Task<InboundMailItem> RouteAsync(string mailId, string toTeamKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(toTeamKey)) throw new ArgumentException("A mail is routed to a team.", nameof(toTeamKey));
        if (_teams != null && await _teams.GetTeamByKeyAsync(toTeamKey) == null) throw new ArgumentException($"There is no team '{toTeamKey}' to route the mail to.", nameof(toTeamKey));

        var routed = await _mail.RouteAsync(mailId, toTeamKey, cancellationToken);
        if (routed != null) _notifier.Notify(new TeamMailUpdatedEventArgs { TeamKey = toTeamKey, MailId = mailId, Change = TeamMailChange.Rerouted });

        return routed;
    }

    /// <remarks>
    /// The store writes only when the mail is still held, so two people archiving at once record the first; the
    /// second is handed the mail as the first left it, and only one change is announced.
    /// </remarks>
    public async Task<InboundMailItem> ArchiveAsync(string mailId, CancellationToken cancellationToken = default)
    {
        var mail = await _mail.GetUnroutedAsync(mailId, cancellationToken);
        if (mail == null || IsArchived(mail)) return mail;

        var archivedBy = _principal == null ? null : await new TeamAuthorizer(_principal).GetDisplayNameAsync();
        var archived = await _mail.ArchiveUnroutedAsync(mailId, archivedBy, _time.GetUtcNow().UtcDateTime, cancellationToken);
        if (archived == null) return await _mail.GetUnroutedAsync(mailId, cancellationToken);

        _notifier.Notify(new TeamMailUpdatedEventArgs { TeamKey = null, MailId = mailId, Change = TeamMailChange.Archived });
        return archived;
    }

    public async Task<InboundMailItem> RestoreAsync(string mailId, CancellationToken cancellationToken = default)
    {
        var mail = await _mail.GetUnroutedAsync(mailId, cancellationToken);
        if (mail == null || !IsArchived(mail)) return mail;

        var restored = await _mail.RestoreUnroutedAsync(mailId, cancellationToken);
        if (restored == null) return await _mail.GetUnroutedAsync(mailId, cancellationToken);

        _notifier.Notify(new TeamMailUpdatedEventArgs { TeamKey = null, MailId = mailId, Change = TeamMailChange.Restored });
        return restored;
    }

    private static bool IsArchived(InboundMailItem mail) => mail.Outcome == PostmasterMailOutcome.Archived;

    private static PostmasterMailSummary Summary(InboundMailItem item) => new()
    {
        Id = item.Id,
        From = item.Envelope.From,
        Subject = item.Envelope.Subject,
        ReceivedAt = item.ReceivedAt,
        Reason = item.Reason,
        ArchivedAt = IsArchived(item) ? item.HandledAt : null,
        ArchivedBy = IsArchived(item) ? item.HandledBy : null
    };
}
