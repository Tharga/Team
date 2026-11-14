using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Tharga.Team.Email;
using Tharga.Team.Email.Users;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Opens an unassigned case for a visitor who wrote through a public form.
/// </summary>
/// <remarks>
/// <b>This follows the mail path for an unknown sender</b> (<c>EmailEventHandler</c>) on purpose: the same unassigned
/// case, the same suggestions, the same Email binding with the sender as the correspondent, so staff answer it as
/// they answer any mailed case and the answer is mailed to the visitor.
/// <para>
/// <b>The address is a claim, not a fact</b>, exactly as a <c>From</c> header is. It is never an identity, and never
/// places the case in a team: it is looked up only so staff are offered the teams it could belong to.
/// </para>
/// <para>
/// <b>Nothing a visitor can do loses their message after it passed the checks.</b> A directory that fails, a receipt
/// that cannot be sent: each is logged and the case stands. Refusals all come first, before anything is written.
/// </para>
/// <para>
/// <b>It authorizes nobody and is meant to.</b> It is reached only through <see cref="IExternalSupportCaseService"/>,
/// which a host calls from a form it has already protected.
/// </para>
/// </remarks>
internal sealed class ExternalSupportCaseService(
    ISupportCaseStore store,
    SupportMailboxes mailboxes,
    TimeProvider timeProvider,
    ISupportCaseNotifier notifier = null,
    ISupportSenderDirectory senderDirectory = null,
    IMailClient mailClient = null,
    SupportExternalCaseOptions options = null,
    ILogger<ExternalSupportCaseService> logger = null,
    IUserMailOutbox userMailOutbox = null,
    MailOutcomeNotifier mailOutcomes = null) : IExternalSupportCaseService
{
    private readonly SupportExternalCaseOptions _options = options ?? new SupportExternalCaseOptions();

    public async Task<SupportCase> RaiseExternalCaseAsync(ExternalSupportContact contact, string subject, string body, CancellationToken cancellationToken = default)
    {
        var (checkedContact, checkedSubject, checkedBody) = Check(contact, subject, body);

        var mailbox = mailboxes.Primary;
        var placement = SupportSenderPlacement.Decide(false, await SendersAsync(checkedContact.Email, cancellationToken));
        var authorName = checkedContact.Name ?? checkedContact.Email;
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var caseId = Guid.NewGuid().ToString();
        var fromAddress = string.IsNullOrWhiteSpace(mailbox.Options.FromAddress) ? null : mailbox.Options.FromAddress.Trim();

        var supportCase = new SupportCase
        {
            Id = caseId,
            TeamKey = null,
            AuthorIdentity = null,
            AuthorName = authorName,
            Subject = checkedSubject ?? SubjectFromMessage.Derive(checkedBody),
            Status = SupportCaseStatus.Open,
            CreatedAt = now,
            MessageCount = 1,
            SuggestedTeams = placement.Suggestions,
            SuggestionReason = placement.Reason,
            ExternalContact = checkedContact,
            Bindings =
            [
                new SupportChannelBinding
                {
                    ChannelType = SupportChannelType.Email,
                    ExternalId = ThreadRootId(caseId, fromAddress),
                    Address = checkedContact.Email,
                    Mailbox = mailbox.Name,
                    MailboxAddress = fromAddress
                }
            ]
        };

        await store.AddCaseAsync(supportCase, new SupportMessage
        {
            Sequence = 1,
            Kind = SupportMessageKind.User,
            AuthorIdentity = null,
            AuthorName = authorName,
            Body = checkedBody,
            SentAt = now,

            // It arrived through the form and the visitor wrote it, so it is already delivered. Marking it
            // Pending would have the reply path mail the visitor their own message back.
            Delivery = SupportMessageDelivery.Sent,
            Source = SupportChannelType.Email
        }, cancellationToken);

        notifier?.Notify(new SupportCaseUpdatedEventArgs
        {
            TeamKey = null,
            CaseId = caseId,
            Change = SupportCaseChange.Raised,
            FromChannel = true
        });

        logger?.LogInformation("Opened unassigned support case {CaseId} from a public form, suggesting {SuggestionCount} team(s).", caseId, placement.Suggestions.Length);

        if (_options.SendReceipt) await SendReceiptAsync(supportCase, mailbox, checkedBody, cancellationToken);

        return supportCase;
    }

    /// <summary>The thread's first id, in the form a mail client sends, so the case can be found by it like any other.</summary>
    /// <remarks>
    /// Nothing was mailed with it unless a receipt goes out, and then the receipt's own id joins the thread. Either way an
    /// answer threads on the ids the case holds, and a reply names one of them.
    /// </remarks>
    internal static string ThreadRootId(string caseId, string fromAddress)
    {
        var at = fromAddress?.LastIndexOf('@') ?? -1;
        var domain = at >= 0 && at < fromAddress.Length - 1 ? fromAddress[(at + 1)..] : "support.invalid";

        return $"{caseId}@{domain}";
    }

    /// <remarks>
    /// <b>A receipt never costs the visitor their case.</b> The case is written first; a mail server that is down, not
    /// configured or refusing is logged, and staff still see the message. Off by default, because a public form that
    /// mails whoever types an address into it is how it ends up used to mail other people. Each attempt is recorded in
    /// the mail log, sent or not, and one recorded as failed is reported to every <see cref="IMailOutcomeSink"/>.
    /// </remarks>
    private async Task SendReceiptAsync(SupportCase supportCase, SupportMailbox mailbox, string body, CancellationToken cancellationToken)
    {
        if (mailClient is not { CanSend: true })
        {
            logger?.LogWarning("The receipt for support case {CaseId} was not sent: no mail server is configured for sending.", supportCase.Id);
            return;
        }

        var subject = $"Re: {supportCase.Subject}";
        string replyTo = null;
        var recorded = false;

        try
        {
            var text = string.IsNullOrWhiteSpace(_options.ReceiptText) ? SupportExternalCaseOptions.DefaultReceiptText : _options.ReceiptText;
            replyTo = await CaseReplyAddress.ForAsync(store, mailbox.Options, supportCase, logger, cancellationToken);

            var result = await mailClient.SendAsync(new OutboundMail(
                To: supportCase.ExternalContact.Email,
                Subject: subject,
                Body: $"{text}\n\n> {body.ReplaceLineEndings("\n> ")}",
                ReplyTo: replyTo), cancellationToken);

            await RecordReceiptAsync(supportCase, mailbox, subject, replyTo, result.Success, result.MessageId, result.Error, cancellationToken);
            recorded = true;

            // The receipt's own id joins the thread, so the visitor's reply to it finds the case.
            if (result.Success && !string.IsNullOrWhiteSpace(result.MessageId))
            {
                await store.AddBindingAsync(supportCase.TeamKey, supportCase.Id, new SupportChannelBinding
                {
                    ChannelType = SupportChannelType.Email,
                    ExternalId = result.MessageId,
                    Address = supportCase.ExternalContact.Email,
                    Mailbox = mailbox.Name,
                    MailboxAddress = supportCase.Bindings[0].MailboxAddress
                }, cancellationToken);
            }
            else if (!result.Success)
            {
                logger?.LogWarning("The receipt for support case {CaseId} was not delivered: {Error}", supportCase.Id, result.Error);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger?.LogWarning(e, "The receipt for support case {CaseId} could not be sent; the case stands.", supportCase.Id);
            if (!recorded) await RecordReceiptAsync(supportCase, mailbox, subject, replyTo, false, null, e.Message, cancellationToken);
        }
    }

    /// <summary>The receipt in the mail log, sent by nobody but the application: sent with its <c>Message-ID</c>, or failed for the reason given.</summary>
    private Task RecordReceiptAsync(SupportCase supportCase, SupportMailbox mailbox, string subject, string replyTo, bool sent, string messageId, string error, CancellationToken cancellationToken)
        => SupportMailLog.RecordAsync(userMailOutbox, mailOutcomes, () => Task.FromResult(new UserMailRecord
        {
            Kind = UserMailKind.Receipt,
            RecipientAddress = supportCase.ExternalContact.Email,
            RecipientName = supportCase.ExternalContact.Name,
            Subject = subject,
            TeamKey = supportCase.TeamKey,
            Headers = new UserMailHeaders(mailbox.Options.FromAddress?.Trim(), string.IsNullOrWhiteSpace(mailbox.Options.FromName) ? null : mailbox.Options.FromName, replyTo),
            Status = sent ? UserMailStatus.Sent : UserMailStatus.Failed,
            MessageId = sent ? messageId : null,
            Reason = sent ? null : error,
            At = timeProvider.GetUtcNow().UtcDateTime,
            CaseId = supportCase.Id
        }), logger, supportCase.Id, cancellationToken);

    /// <summary>The accounts holding the address, or none when there is no directory or it fails; only ever for suggestions.</summary>
    private async Task<SupportSender[]> SendersAsync(string address, CancellationToken cancellationToken)
    {
        if (senderDirectory == null) return [];

        try
        {
            return await senderDirectory.FindAsync(address, cancellationToken) ?? [];
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger?.LogWarning(e, "The address of a public support form could not be looked up, so its case opens without suggestions.");

            return [];
        }
    }

    /// <summary>Every refusal, before anything is written; returns what to keep, trimmed.</summary>
    internal static (ExternalSupportContact Contact, string Subject, string Body) Check(ExternalSupportContact contact, string subject, string body)
    {
        ArgumentNullException.ThrowIfNull(contact);

        var email = contact.Email?.Trim();
        if (string.IsNullOrEmpty(email)) throw new ArgumentException("An email address is required.", nameof(contact));
        if (email.Length > SupportCaseLimits.MaxExternalEmailLength) throw new ArgumentException($"The email address is longer than {SupportCaseLimits.MaxExternalEmailLength} characters.", nameof(contact));
        if (!IsPlainAddress(email)) throw new ArgumentException("The email address is not a single, plain address.", nameof(contact));

        var name = Bounded(contact.Name, SupportCaseLimits.MaxExternalNameLength, "name");
        var phone = Bounded(contact.Phone, SupportCaseLimits.MaxExternalPhoneLength, "phone number");
        var organisation = Bounded(contact.Organisation, SupportCaseLimits.MaxExternalNameLength, "organisation");
        var fields = CheckedFields(contact.Fields);

        var checkedSubject = Bounded(subject, SupportCaseLimits.MaxExternalSubjectLength, "subject");

        if (string.IsNullOrWhiteSpace(body)) throw new ArgumentException("A message is required.", nameof(body));
        var text = body.Trim();
        if (text.Length > SupportCaseLimits.MaxMessageLength) throw new ArgumentException($"The message is longer than {SupportCaseLimits.MaxMessageLength} characters.", nameof(body));

        return (contact with { Email = email, Name = name, Phone = phone, Organisation = organisation, Fields = fields }, checkedSubject, text);
    }

    /// <summary>Trimmed, and null when empty; refused past <paramref name="max"/>.</summary>
    private static string Bounded(string value, int max, string what)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;
        if (trimmed.Length > max) throw new ArgumentException($"The {what} is longer than {max} characters.");

        return trimmed;
    }

    private static Dictionary<string, string> CheckedFields(IReadOnlyDictionary<string, string> fields)
    {
        var kept = new Dictionary<string, string>();
        if (fields == null) return kept;
        if (fields.Count > SupportCaseLimits.MaxExternalFields) throw new ArgumentException($"There are more than {SupportCaseLimits.MaxExternalFields} extra fields.");

        foreach (var (key, value) in fields)
        {
            var name = key?.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            if (name.Length > SupportCaseLimits.MaxExternalFieldNameLength) throw new ArgumentException($"A field name is longer than {SupportCaseLimits.MaxExternalFieldNameLength} characters.");
            if (value?.Length > SupportCaseLimits.MaxExternalFieldValueLength) throw new ArgumentException($"The value of '{name}' is longer than {SupportCaseLimits.MaxExternalFieldValueLength} characters.");

            kept[name] = value?.Trim();
        }

        return kept;
    }

    /// <summary>One bare address: no display name, no list, no line breaks to smuggle a header in.</summary>
    private static bool IsPlainAddress(string email)
        => email.All(c => !char.IsWhiteSpace(c) && !char.IsControl(c) && c is not (',' or ';' or '<' or '>'))
           && MailAddress.TryCreate(email, out var parsed)
           && string.Equals(parsed.Address, email, StringComparison.Ordinal);
}
