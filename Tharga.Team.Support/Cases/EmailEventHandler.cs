using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Support.Email;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Applies one received mail to the case it belongs to.
/// </summary>
/// <remarks>
/// <b>The order of the checks below is load-bearing, and it deviates from <see cref="SlackEventHandler"/>
/// deliberately — do not align them.</b> That handler records the event in <see cref="ISupportEventLedger"/>
/// before its own filters, which is right for Slack: one workspace, one instance, and a duplicate should be
/// dropped. Here the mailbox may be shared by two sites, and the ledger deduplicates on
/// <c>(source, message id)</c> in a database those sites may also share. Recording first would mean this
/// instance claims a mail addressed to the other site, drops it, and the other instance then sees a duplicate
/// and concludes somebody handled it. Nobody did, and the mail is gone with a log line saying it was already
/// dealt with.
/// <para>
/// <b>Nothing here writes to the mailbox.</b> No flag, no move. A message this instance ignores has to stay
/// findable by the one that wants it.
/// </para>
/// </remarks>
internal sealed class EmailEventHandler(
    ISupportCaseStore store,
    ISupportEventLedger ledger,
    IOptions<MailOptions> options,
    TimeProvider timeProvider,
    ISupportCaseNotifier notifier = null,
    ILogger<EmailEventHandler> logger = null)
{
    /// <summary>Ledger source, so an id from a mailbox cannot collide with one from Slack.</summary>
    internal const string Source = "email";

    public async Task<EmailHandlingOutcome> HandleAsync(InboundMail mail, CancellationToken cancellationToken = default)
    {
        var mailOptions = options.Value;

        if (string.IsNullOrWhiteSpace(mail.MessageId)) return EmailHandlingOutcome.Ignored("no message id");

        // Before the ledger. See the remarks above -- this is the check that must not move.
        if (!new RecipientFilter(mailOptions.Recipients).AcceptsAny(mail.DeliveredTo))
        {
            logger?.LogDebug("Ignored a mail addressed to {Recipients}, which this instance does not answer for.",
                string.Join(", ", mail.DeliveredTo));

            return EmailHandlingOutcome.Ignored("addressed elsewhere");
        }

        // Answering one of these is how a support case and a vacation responder fill each other's mailboxes
        // overnight.
        if (mail.IsAutomated) return EmailHandlingOutcome.Ignored("automated");

        // Our own mail, arriving back. Reading it as a reply would append everything support sends a second
        // time, attributed to support.
        if (IsOurOwn(mail, mailOptions)) return EmailHandlingOutcome.Ignored("sent by this instance");

        if (!await ledger.TryRecordAsync(Source, mail.MessageId, cancellationToken))
        {
            logger?.LogDebug("Ignored a mail that had already been handled.");

            return EmailHandlingOutcome.Ignored("already handled");
        }

        var supportCase = await MatchAsync(mail, cancellationToken);

        if (supportCase == null) return EmailHandlingOutcome.Ignored("no matching case");

        var binding = supportCase.Bindings.FirstOrDefault(x => x.ChannelType == SupportChannelType.Email);

        // A From header authenticates nobody. Accepting mail from anyone who can name a thread id would let
        // a stranger write into a transcript a real person reads and trusts.
        if (!Corresponds(binding, mail.From))
        {
            logger?.LogWarning("Ignored a mail on case {CaseId}: it came from {Sender}, which is not the address the case corresponds with.",
                supportCase.Id, mail.From);

            return EmailHandlingOutcome.Ignored("sender does not correspond with the case");
        }

        var body = Body(mail);

        if (string.IsNullOrWhiteSpace(body)) return EmailHandlingOutcome.Ignored("empty body");

        await store.AppendMessageAsync(supportCase.TeamKey, supportCase.Id, new SupportMessage
        {
            Sequence = 0,
            Kind = SupportMessageKind.User,
            AuthorIdentity = mail.From,
            AuthorName = mail.From,
            Body = body,
            SentAt = timeProvider.GetUtcNow().UtcDateTime,

            // It came from the channel, so it is already there. Posting it back would echo it into the
            // thread it arrived from.
            Delivery = SupportMessageDelivery.Sent,
            Source = SupportChannelType.Email
        }, cancellationToken);

        notifier?.Notify(new SupportCaseUpdatedEventArgs
        {
            TeamKey = supportCase.TeamKey,
            CaseId = supportCase.Id,
            Change = SupportCaseChange.Replied,
            FromChannel = true
        });

        return EmailHandlingOutcome.Applied(supportCase.Id);
    }

    private static bool IsOurOwn(InboundMail mail, MailOptions options)
        => !string.IsNullOrWhiteSpace(options.FromAddress) &&
           string.Equals(mail.From, options.FromAddress.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Finds the case this mail belongs to, by thread first and by addressed-to second.
    /// </summary>
    /// <remarks>
    /// <b>Threading headers first, because they are exact.</b> <c>In-Reply-To</c> names one message and
    /// <c>References</c> names the chain, so the newest is tried first and the opening mail last.
    /// <para>
    /// <b>The per-case address is the fallback for clients that drop those headers</b>, which is common
    /// enough to matter and impossible to detect from this side. It only works when the host turned
    /// <see cref="MailOptions.PerCaseReplyTo"/> on, so the headers remain the primary mechanism.
    /// </para>
    /// </remarks>
    private async Task<SupportCase> MatchAsync(InboundMail mail, CancellationToken cancellationToken)
    {
        foreach (var threadId in ThreadIds(mail))
        {
            var found = await store.GetCaseByBindingAsync(SupportChannelType.Email, threadId, cancellationToken);

            if (found != null) return found;
        }

        var caseId = PerCaseAddress.CaseIdIn(mail.DeliveredTo, options.Value.FromAddress);

        return caseId == null ? null : await store.GetCaseByIdAsync(caseId, cancellationToken);
    }

    private static IEnumerable<string> ThreadIds(InboundMail mail)
    {
        if (!string.IsNullOrWhiteSpace(mail.InReplyTo)) yield return mail.InReplyTo;

        foreach (var reference in (mail.References ?? []).Reverse())
        {
            if (!string.IsNullOrWhiteSpace(reference) && reference != mail.InReplyTo) yield return reference;
        }
    }

    private static bool Corresponds(SupportChannelBinding binding, string sender)
        => !string.IsNullOrWhiteSpace(binding?.Address) &&
           !string.IsNullOrWhiteSpace(sender) &&
           string.Equals(binding.Address.Trim(), sender.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Body(InboundMail mail)
    {
        var text = QuotedText.Trim(mail.Body);

        return mail.HadAttachments ? $"{text}\n\n[attachments were not stored]".TrimStart() : text;
    }
}

/// <summary>What one received mail resulted in.</summary>
/// <param name="WasApplied">True when it was appended to a case.</param>
/// <param name="CaseId">The case it reached, or null.</param>
/// <param name="Reason">Why it was ignored, or null when it was applied.</param>
/// <remarks>
/// Ignoring is the common outcome and is not a failure: a shared mailbox carries mail for other sites, for
/// other systems and for people. It is reported rather than thrown so the poller can log a count instead of
/// treating a busy mailbox as a series of errors.
/// </remarks>
internal readonly record struct EmailHandlingOutcome(bool WasApplied, string CaseId, string Reason)
{
    public static EmailHandlingOutcome Ignored(string reason) => new(false, null, reason);

    public static EmailHandlingOutcome Applied(string caseId) => new(true, caseId, null);
}
