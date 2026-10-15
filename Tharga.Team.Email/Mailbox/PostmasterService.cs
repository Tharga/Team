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

    public PostmasterService(ITeamMailStore mail, ITeamMailNotifier notifier, IInboundMailContentStore content = null, ITeamService teams = null)
    {
        _mail = mail;
        _notifier = notifier;
        _content = content;
        _teams = teams;
    }

    public async Task<PostmasterPage> ListAsync(string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default)
    {
        var page = await _mail.ListUnroutedAsync(cursor, pageSize, cancellationToken);

        return new PostmasterPage
        {
            Items = [.. page.Items.Select(x => new PostmasterMailSummary { Id = x.Id, From = x.Envelope.From, Subject = x.Envelope.Subject, ReceivedAt = x.ReceivedAt, Reason = x.Reason })],
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
}
