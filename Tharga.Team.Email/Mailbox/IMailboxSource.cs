namespace Tharga.Team.Email.Mailbox;

/// <summary>Where the team mailbox reads from: the catch-all mailbox, each message with its raw content and UID.</summary>
internal interface IMailboxSource
{
    bool CanRead { get; }

    Task<MailFetchResult> FetchAsync(MailFetchPosition position, CancellationToken cancellationToken);
}
