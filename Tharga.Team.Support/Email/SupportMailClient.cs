using MailKit;
using MailKit.Net.Imap;
using MailKit.Net.Smtp;
using MailKit.Search;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Tharga.Team.Support.Email;

/// <summary>
/// Sends over SMTP and reads over IMAP, using MailKit.
/// </summary>
/// <remarks>
/// <b>Never throws</b>, matching <c>SlackClient</c>. A failed send comes back as a failed
/// <see cref="MailSendResult"/> and a failed read as an empty fetch at the unchanged position, so a mail
/// server being down delays a conversation instead of breaking the application that hosts it.
/// <para>
/// <b>A connection per operation.</b> Both clients are opened, used and disposed rather than pooled. A
/// long-lived IMAP connection is the shape this design rejected when it chose polling over a socket: it is
/// process-local state that has to be revived after every network blip, and it buys nothing at a poll
/// interval measured in minutes.
/// </para>
/// </remarks>
internal sealed class SupportMailClient(IOptions<MailOptions> options, ILogger<SupportMailClient> logger = null) : ISupportMailClient
{
    /// <summary>How many messages one poll will take, so a large backlog cannot stall a deployment.</summary>
    private const int MaxMessagesPerFetch = 100;

    private readonly MailOptions _options = options?.Value ?? new MailOptions();

    public bool CanSend => _options.Smtp.IsConfigured && !string.IsNullOrWhiteSpace(_options.FromAddress);

    public bool CanRead => _options.Imap.IsConfigured;

    public async Task<MailSendResult> SendAsync(OutboundMail mail, CancellationToken cancellationToken = default)
    {
        if (!CanSend) return MailSendResult.Failed("Support email is not configured for sending.");
        if (string.IsNullOrWhiteSpace(mail.To)) return MailSendResult.Failed("No recipient.");

        try
        {
            var message = MailMessageFactory.Create(mail, _options);

            using var client = new SmtpClient { Timeout = (int)_options.Timeout.TotalMilliseconds };

            await ConnectAsync(client, _options.Smtp, cancellationToken);
            await client.SendAsync(message, cancellationToken);
            await client.DisconnectAsync(true, cancellationToken);

            return MailSendResult.Ok(message.MessageId);
        }
        catch (Exception e)
        {
            logger?.LogError(e, "Support mail to {Recipient} could not be sent.", mail.To);

            return MailSendResult.Failed(e.Message);
        }
    }

    public async Task<MailFetchResult> FetchAsync(MailFetchPosition position, CancellationToken cancellationToken = default)
    {
        if (!CanRead) return MailFetchResult.Empty(position);

        try
        {
            using var client = new ImapClient { Timeout = (int)_options.Timeout.TotalMilliseconds };

            await ConnectAsync(client, _options.Imap, cancellationToken);

            var folder = await client.GetFolderAsync(_options.Folder ?? "INBOX", cancellationToken);

            // ReadOnly is the guarantee, not merely the intent: opening read-write is what lets a fetch set
            // \Seen as a side effect, which would hide the mail from the other application reading this
            // mailbox for its own domain.
            await folder.OpenAsync(FolderAccess.ReadOnly, cancellationToken);

            var rescanned = position.IsInvalidatedBy(folder.UidValidity);
            var from = rescanned ? 0 : position.LastUid;

            var uids = await folder.SearchAsync(SearchQuery.Uids(new UniqueIdRange(new UniqueId(from + 1), UniqueId.MaxValue)), cancellationToken);

            var mails = new List<InboundMail>();
            var highest = from;

            foreach (var uid in uids.Take(MaxMessagesPerFetch))
            {
                var message = await folder.GetMessageAsync(uid, cancellationToken);

                mails.Add(InboundMailReader.Read(message));
                highest = uid.Id;
            }

            await client.DisconnectAsync(true, cancellationToken);

            return new MailFetchResult(new MailFetchPosition(folder.UidValidity, highest), mails, rescanned);
        }
        catch (Exception e)
        {
            logger?.LogError(e, "The support mailbox could not be read.");

            return MailFetchResult.Empty(position);
        }
    }

    /// <remarks>
    /// Port zero asks MailKit for the protocol's default, which is why <see cref="MailServerOptions.Port"/>
    /// is nullable rather than carrying a guessed constant per protocol.
    /// </remarks>
    private static async Task ConnectAsync(IMailService client, MailServerOptions server, CancellationToken cancellationToken)
    {
        await client.ConnectAsync(server.Host, server.Port ?? 0, server.UseSsl, cancellationToken);

        if (!string.IsNullOrWhiteSpace(server.UserName))
        {
            await client.AuthenticateAsync(server.UserName, server.Password, cancellationToken);
        }
    }
}
