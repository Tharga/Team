using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Mailbox;

/// <summary>
/// Takes one mail from the catch-all mailbox to the team it was delivered to, or to the postmaster.
/// </summary>
/// <remarks>
/// <b>Content first, then the record.</b> The raw message is saved before the mail is recorded, so a crash in
/// between leaves content nobody points at — harmless, and overwritten by the same id on the re-read — and
/// never a recorded mail with nothing behind it. The record's unique id is what makes a re-read, or a second
/// instance reading the same mail, store it once.
/// </remarks>
internal sealed class TeamMailReceiver
{
    private readonly ITeamMailKeyStore _keys;
    private readonly ITeamMailStore _mail;
    private readonly IInboundMailContentStore _content;
    private readonly ITeamMailNotifier _notifier;
    private readonly TeamMailboxOptions _options;
    private readonly TimeProvider _time;

    public TeamMailReceiver(ITeamMailKeyStore keys, ITeamMailStore mail, IInboundMailContentStore content, ITeamMailNotifier notifier, IOptions<TeamMailboxOptions> options, TimeProvider time)
    {
        _keys = keys;
        _mail = mail;
        _content = content;
        _notifier = notifier;
        _options = options.Value;
        _time = time;
    }

    /// <summary>Keeps the mail; false when it was already kept, by this instance or another.</summary>
    public async Task<bool> ReceiveAsync(InboundMail mail, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        var route = await MailRoute.DecideAsync(mail.DeliveredTo ?? [], _options, key => _keys.FindAsync(key, cancellationToken), now);
        var id = InboundMailId.For(mail.MessageId, route.DeliveredTo, mail.Raw);

        await _content.SaveAsync(id, mail.Raw ?? [], cancellationToken);

        var added = await _mail.AddAsync(new InboundMailItem
        {
            Id = id,
            TeamKey = route.TeamKey,
            ItemKey = route.ItemKey,
            Envelope = new MailEnvelope
            {
                MessageId = mail.MessageId ?? string.Empty,
                From = mail.From ?? string.Empty,
                FromName = string.IsNullOrWhiteSpace(mail.FromName) ? null : mail.FromName,
                To = [.. mail.To ?? []],
                Cc = [.. mail.Cc ?? []],
                DeliveredTo = route.DeliveredTo ?? string.Empty,
                Subject = mail.Subject,
                SentAt = mail.SentAt == default ? null : mail.SentAt.UtcDateTime,
                InReplyTo = mail.InReplyTo,
                References = [.. mail.References ?? []],
                HasAttachments = mail.HadAttachments,
                IsAutomated = mail.IsAutomated
            },
            Reason = route.Reason,
            ReceivedAt = now,
            KeyRetiresAt = route.KeyRetiresAt
        }, cancellationToken);

        if (added) _notifier.Notify(new TeamMailUpdatedEventArgs { TeamKey = route.TeamKey, MailId = id, Change = TeamMailChange.Received });

        return added;
    }
}
