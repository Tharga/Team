using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Tharga.Team.Service.Email;

namespace Tharga.Team.Email.Users;

/// <summary>Sends queued mail to users, one claimed mail at a time, and records what became of each.</summary>
/// <remarks>
/// <b>Everything about the recipient is decided at send time</b>: whether they still receive the category, and the
/// address on their user record. A mail they opted out of after it was queued is skipped, not failed. A mail queued to
/// an address alone, which only a required category takes, goes to that address.
/// <para>
/// <b>Contact mail goes its own way through the same queue</b>: to the address it was queued with, unless that address has
/// opted out of its category or of all contact mail by the time it is sent, from the contact sender and with the contact
/// footer. Without contact mail registered it fails with the reason rather than going out as mail to users.
/// </para>
/// <para>
/// <b>Only a failure that may pass is retried</b> — the server refusing or being unreachable, a store that could not
/// be read. A user with no address, a category no longer registered or a mail the non-production policy withholds
/// would fail the same way every time, so they fail at once with the reason. The recipient's address is never logged.
/// </para>
/// </remarks>
internal sealed class UserMailDispatcher(
    IUserMailOutbox outbox,
    UserMailCategoryRegistry categories,
    UserMailPreferences preferences,
    IUserMailRecipientDirectory recipients,
    IMailClient mailClient,
    IOutboundMailPolicy policy,
    UserMailFooter footer,
    IOptions<UserMailOptions> options,
    TimeProvider timeProvider,
    ILogger<UserMailDispatcher> logger = null,
    MailTemplate template = null,
    ContactMailSending contacts = null)
{
    internal const string OptedOutReason = "The recipient does not receive this category.";
    internal const string WithheldReason = "Withheld by the non-production mail policy: set Email:Override to receive it.";
    internal const string BatchGoneReason = "The content this mail shares with others is no longer stored.";
    internal const string AddressAloneReason = "A mail to an address alone is sent only in a required category.";
    internal const string ContactsNotRegisteredReason = "Contact mail is not registered here, so it is not sent: register it with AddThargaContactMessages.";
    internal const string ContactOptedOutReason = "The address opted out of this kind of mail.";
    internal const string ContactOptedOutOfAllReason = "The address opted out of all mail from the product.";

    private readonly Dictionary<string, UserMailBatch> _batches = new(StringComparer.Ordinal);
    private readonly UserMailComposer _composer = new(footer, template);

    /// <summary>Sends claimed mail until none is due or a batch is done, and returns how many were handled.</summary>
    public async Task<int> RunAsync(string claimant, CancellationToken cancellationToken)
    {
        var handled = 0;
        var batch = Math.Max(1, options.Value.BatchSize);

        while (handled < batch && !cancellationToken.IsCancellationRequested)
        {
            if (await SendNextAsync(claimant, cancellationToken) == null) break;
            handled++;
        }

        return handled;
    }

    /// <summary>Claims and handles the next due mail, returning the status recorded for it, or null when none was due.</summary>
    public async Task<UserMailStatus?> SendNextAsync(string claimant, CancellationToken cancellationToken)
    {
        var o = options.Value;
        var item = await outbox.ClaimNextAsync(claimant, Now(), o.Lease, cancellationToken);
        if (item == null) return null;

        try
        {
            return await SendAsync(item, claimant, true, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(e, "User mail {Id} could not be handled.", item.Id);
            return await RetryAsync(item, claimant, e.Message, true, cancellationToken);
        }
    }

    /// <summary>Makes the one attempt at a mail the caller already holds, recording it sent or failed and never leaving it to be tried again.</summary>
    /// <remarks>For a caller who reports the outcome to someone waiting for it: a mail they were told did not go must not go later.</remarks>
    public async Task<UserMailStatus> SendNowAsync(UserMailItem item, string claimant, CancellationToken cancellationToken)
    {
        try
        {
            return await SendAsync(item, claimant, false, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger?.LogWarning(e, "User mail {Id} could not be sent.", item.Id);
            return await FailAsync(item, claimant, e.Message, cancellationToken);
        }
    }

    private async Task<UserMailStatus> SendAsync(UserMailItem item, string claimant, bool retry, CancellationToken cancellationToken)
    {
        var mail = await ContentOfAsync(item, cancellationToken);
        if (mail == null) return await FailAsync(item, claimant, BatchGoneReason, cancellationToken);
        if (mail.Kind == UserMailKind.Contact) return await SendToContactAsync(item, mail, claimant, retry, cancellationToken);

        var category = categories.Find(mail.Category);
        if (category == null) return await FailAsync(item, claimant, $"The mail category '{mail.Category}' is no longer registered.", cancellationToken);

        if (item.Attempts > options.Value.MaxAttempts) return await FailAsync(item, claimant, GaveUp(item.Attempts - 1, item.Reason), cancellationToken);

        UserMailRecipient recipient;
        if (mail.RecipientUserKey == null)
        {
            if (!category.Required) return await FailAsync(item, claimant, AddressAloneReason, cancellationToken);
            if (string.IsNullOrWhiteSpace(mail.RecipientAddress)) return await FailAsync(item, claimant, "The mail names neither a user nor an address.", cancellationToken);

            recipient = new UserMailRecipient { Key = null, EMail = mail.RecipientAddress, Name = mail.RecipientName };
        }
        else
        {
            if (!await preferences.ReceivesAsync(mail.RecipientUserKey, category, cancellationToken))
            {
                await RecordAsync(outbox.MarkSkippedAsync(item.Id, claimant, OptedOutReason, Now(), cancellationToken), item);
                return UserMailStatus.Skipped;
            }

            recipient = await recipients.FindAsync(mail.RecipientUserKey, cancellationToken);
            if (recipient == null) return await FailAsync(item, claimant, "No user has this key.", cancellationToken);
            if (string.IsNullOrWhiteSpace(recipient.EMail)) return await FailAsync(item, claimant, "The user record holds no address.", cancellationToken);
        }

        var decision = Decide(item, recipient.EMail.Trim(), mail.Subject);
        if (!decision.ShouldSend) return await FailAsync(item, claimant, WithheldReason, cancellationToken);

        var composed = await _composer.ComposeAsync(mail.Subject, mail.Body, mail.BodyHtml, mail.RecipientUserKey, category);

        return await DeliverAsync(item, claimant, mailClient, Outbound(mail, composed, recipient, decision, mail.ReplyTo ?? options.Value.ReplyTo), recipient.EMail, retry, cancellationToken);
    }

    /// <summary>A contact mail: to its address unless that has opted out by now, from the contact sender with the contact footer.</summary>
    private async Task<UserMailStatus> SendToContactAsync(UserMailItem item, UserMail mail, string claimant, bool retry, CancellationToken cancellationToken)
    {
        if (contacts == null) return await FailAsync(item, claimant, ContactsNotRegisteredReason, cancellationToken);

        var category = contacts.Categories.Find(mail.Category);
        if (category == null) return await FailAsync(item, claimant, $"The contact mail category '{mail.Category}' is no longer registered.", cancellationToken);

        if (item.Attempts > options.Value.MaxAttempts) return await FailAsync(item, claimant, GaveUp(item.Attempts - 1, item.Reason), cancellationToken);

        var address = UserMailAddresses.Normalize(mail.RecipientAddress);
        if (address == null) return await FailAsync(item, claimant, "The mail names no address.", cancellationToken);

        var suppression = await contacts.SuppressionOfAsync(address, category.Key, cancellationToken);
        if (suppression != null)
        {
            await RecordAsync(outbox.MarkSkippedAsync(item.Id, claimant, suppression.Category == null ? ContactOptedOutOfAllReason : ContactOptedOutReason, Now(), cancellationToken), item);
            return UserMailStatus.Skipped;
        }

        var decision = Decide(item, address, mail.Subject);
        if (!decision.ShouldSend) return await FailAsync(item, claimant, WithheldReason, cancellationToken);

        var composed = _composer.Compose(mail.Subject, mail.Body, mail.BodyHtml, await contacts.Footer.ForAsync(address, category));
        var recipient = new UserMailRecipient { Key = null, EMail = address, Name = mail.RecipientName };

        return await DeliverAsync(item, claimant, contacts.Mail, Outbound(mail, composed, recipient, decision, contacts.ReplyTo), address, retry, cancellationToken);
    }

    /// <summary>What the outbound mail policy does with this mail, logged when it is withheld or redirected.</summary>
    private OutboundMailDecision Decide(UserMailItem item, string address, string subject)
    {
        var decision = policy.Decide(address, subject);

        if (!decision.ShouldSend) logger?.LogWarning("User mail {Id} was not sent. {Reason}", item.Id, WithheldReason);
        else if (decision.Action == OutboundMailAction.Redirect)
            logger?.LogInformation("Redirecting user mail {Id} to {Recipient} instead of the address it was for, because the environment is not production.", item.Id, decision.Recipient);

        return decision;
    }

    /// <summary>Sends the mail and records it sent with the address it was addressed to, or leaves it to be tried again.</summary>
    private async Task<UserMailStatus> DeliverAsync(UserMailItem item, string claimant, IMailClient client, OutboundMail outbound, string address, bool retry, CancellationToken cancellationToken)
    {
        var result = await client.SendAsync(outbound, cancellationToken);
        if (!result.Success) return await RetryAsync(item, claimant, result.Error, retry, cancellationToken);

        await RecordAsync(outbox.MarkSentAsync(item.Id, claimant, result.MessageId, UserMailAddresses.Normalize(address), Now(), cancellationToken), item);
        return UserMailStatus.Sent;
    }

    /// <summary>The mail with its content: its own, or for a mail of a batch the batch's, read once per pass; null when the batch is gone.</summary>
    private async Task<UserMail> ContentOfAsync(UserMailItem item, CancellationToken cancellationToken)
    {
        if (item.BatchId == null) return item.Mail;

        if (!_batches.TryGetValue(item.BatchId, out var batch))
        {
            batch = await outbox.GetBatchContentAsync(item.BatchId, cancellationToken);
            _batches[item.BatchId] = batch;
        }

        return batch == null ? null : item.Mail with
        {
            Subject = batch.Subject,
            BodyHtml = batch.BodyHtml,
            Body = batch.Body,
            Attachments = batch.Attachments ?? []
        };
    }

    private static OutboundMail Outbound(UserMail mail, UserMailComposed composed, UserMailRecipient recipient, OutboundMailDecision decision, string replyTo)
    {
        var to = decision.Action == OutboundMailAction.Deliver && !string.IsNullOrWhiteSpace(recipient.Name)
            ? new MailboxAddress(recipient.Name, decision.Recipient).ToString()
            : decision.Recipient;

        return new OutboundMail(
            to,
            decision.Subject,
            composed.Body,
            ReplyTo: replyTo,
            Attachments: [.. (mail.Attachments ?? []).Where(x => x.Content != null).Select(x => new MailAttachment(x.FileName, x.ContentType, x.Content, x.ContentId))],
            BodyHtml: composed.BodyHtml,
            UnsubscribeUrl: composed.UnsubscribeUrl);
    }

    private async Task<UserMailStatus> RetryAsync(UserMailItem item, string claimant, string reason, bool retry, CancellationToken cancellationToken)
    {
        var o = options.Value;
        if (!retry) return await FailAsync(item, claimant, reason, cancellationToken);
        if (item.Attempts >= o.MaxAttempts) return await FailAsync(item, claimant, GaveUp(item.Attempts, reason), cancellationToken);

        await RecordAsync(outbox.PostponeAsync(item.Id, claimant, reason, Now() + Backoff(item.Attempts, o), cancellationToken), item);
        return UserMailStatus.Queued;
    }

    private async Task<UserMailStatus> FailAsync(UserMailItem item, string claimant, string reason, CancellationToken cancellationToken)
    {
        await RecordAsync(outbox.MarkFailedAsync(item.Id, claimant, reason, Now(), cancellationToken), item);
        return UserMailStatus.Failed;
    }

    private async Task RecordAsync(Task<bool> write, UserMailItem item)
    {
        if (!await write) logger?.LogWarning("User mail {Id} was taken by another sender before its outcome was recorded here; the other sender's outcome stands.", item.Id);
    }

    /// <summary>The wait after the given number of attempts: the retry delay, doubled after each further attempt, never longer than the maximum.</summary>
    internal static TimeSpan Backoff(int attempts, UserMailOptions o)
    {
        var exponent = Math.Clamp(attempts - 1, 0, 30);
        var delay = TimeSpan.FromTicks((long)Math.Min(o.RetryDelay.Ticks * Math.Pow(2, exponent), o.MaxRetryDelay.Ticks));

        return delay < TimeSpan.Zero ? TimeSpan.Zero : delay;
    }

    private static string GaveUp(int attempts, string reason) => $"Gave up after {attempts} attempts. {reason}".Trim();

    private DateTime Now() => timeProvider.GetUtcNow().UtcDateTime;
}
