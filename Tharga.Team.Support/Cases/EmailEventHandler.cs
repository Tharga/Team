using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Tharga.Team.Email;
using Tharga.Team.Service.Html;

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
    ILogger<EmailEventHandler> logger = null,
    Tharga.Team.Service.Email.IOutboundMailPolicy policy = null,
    ISupportSenderDirectory senderDirectory = null,
    IOptions<SupportCaseOptions> caseOptions = null,
    ISupportAttachmentStore attachmentStore = null,
    SupportMailboxes mailboxes = null,
    IEnumerable<IMailBounceHandler> bounceHandlers = null)
{
    /// <summary>Ledger source, so an id from a mailbox cannot collide with one from Slack.</summary>
    /// <remarks>One source for every mailbox, so a mail that reached two of them is applied once.</remarks>
    internal const string Source = "email";

    private readonly SupportMailboxes _mailboxes = mailboxes ?? new SupportMailboxes(options.Value);

    public Task<EmailHandlingOutcome> HandleAsync(InboundMail mail, CancellationToken cancellationToken = default)
        => HandleAsync(mail, _mailboxes.Primary, cancellationToken);

    public async Task<EmailHandlingOutcome> HandleAsync(InboundMail mail, SupportMailbox mailbox, CancellationToken cancellationToken = default)
    {
        var mailOptions = mailbox.Options;

        if (string.IsNullOrWhiteSpace(mail.MessageId)) return EmailHandlingOutcome.Ignored("no message id");

        // Before the ledger. See the remarks above -- this is the check that must not move.
        if (!new RecipientFilter(mailOptions.Recipients).AcceptsAny(mail.DeliveredTo))
        {
            logger?.LogDebug("Ignored a mail addressed to {Recipients}, which this instance does not answer for.",
                string.Join(", ", mail.DeliveredTo));

            return EmailHandlingOutcome.Ignored("addressed elsewhere");
        }

        // A bounce is automated too, but it is news about an answer rather than noise, so it is read first.
        if (!string.IsNullOrWhiteSpace(mail.BouncedMessageId)) return await BounceAsync(mail, cancellationToken);

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

        var supportCase = await MatchAsync(mail, mailOptions, cancellationToken);

        // Mail that belongs to no existing case is somebody asking for help for the first time, so it opens
        // one rather than being dropped. Which team it opens in is RaiseAsync's to decide, and its answer is
        // usually none.
        if (supportCase == null) return await RaiseAsync(mail, mailbox, cancellationToken);

        var binding = supportCase.Bindings.FirstOrDefault(x => x.ChannelType == SupportChannelType.Email);

        // A From header authenticates nobody. Accepting mail from anyone who can name a thread id would let
        // a stranger write into a transcript a real person reads and trusts.
        if (!Corresponds(binding, mail.From))
        {
            logger?.LogWarning("Ignored a mail on case {CaseId}: it came from {Sender}, which is not the address the case corresponds with.",
                supportCase.Id, mail.From);

            return EmailHandlingOutcome.Ignored("sender does not correspond with the case");
        }

        var text = QuotedText.Trim(mail.Body, out var textCut);

        if (IsEmpty(mail, text)) return EmailHandlingOutcome.Ignored("empty body");

        var files = await StoreFilesAsync(supportCase.Id, mail, mailOptions, cancellationToken);
        var body = Body(text, files.Notes);
        var html = Html(mail.BodyHtml, textCut, files.Notes);

        await store.AppendMessageAsync(supportCase.TeamKey, supportCase.Id, new SupportMessage
        {
            Sequence = 0,
            Kind = SupportMessageKind.User,

            // The correspondent is the case's author, so the reply carries the author's identity -- none for a
            // case opened unattributed. Anything else and "support wrote last" would read the customer's own
            // reply as an answer, arming the clock that closes their case.
            AuthorIdentity = supportCase.AuthorIdentity,
            AuthorName = mail.From,
            Body = body,
            BodyHtml = html,
            SentAt = timeProvider.GetUtcNow().UtcDateTime,

            // It came from the channel, so it is already there. Posting it back would echo it into the
            // thread it arrived from.
            Delivery = SupportMessageDelivery.Sent,
            Source = SupportChannelType.Email,
            Attachments = files.Stored
        }, cancellationToken);

        // The reply's own id joins the thread, so an answer to it, and a reply to that, find the case exactly.
        await store.AddBindingAsync(supportCase.TeamKey, supportCase.Id, new SupportChannelBinding
        {
            ChannelType = SupportChannelType.Email,
            ExternalId = mail.MessageId,
            Address = binding.Address,
            Mailbox = binding.Mailbox,
            MailboxAddress = binding.MailboxAddress
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

    /// <summary>
    /// Marks the answer a bounce reports as not delivered, with the reason the far server gave.
    /// </summary>
    /// <remarks>
    /// <b>Never a case.</b> A bounce that names no answer this instance sent is somebody else's — another
    /// site sharing the mailbox, mail to users sent from this address, or mail sent before ids were recorded —
    /// and opening a case from it would put a mail server's complaint in the queue as if a customer had
    /// written. Such a bounce is handed to the registered <see cref="IMailBounceHandler"/>s. The reason is kept
    /// on the entry rather than logged, because it often quotes the recipient's address.
    /// </remarks>
    private async Task<EmailHandlingOutcome> BounceAsync(InboundMail mail, CancellationToken cancellationToken)
    {
        var supportCase = await store.GetCaseByBindingAsync(SupportChannelType.Email, mail.BouncedMessageId, cancellationToken);
        var sequence = supportCase?.Bindings?.FirstOrDefault(x => x.ChannelType == SupportChannelType.Email && x.ExternalId == mail.BouncedMessageId)?.MessageSequence;

        if (sequence == null)
        {
            if (await HandOverAsync(mail, cancellationToken)) return EmailHandlingOutcome.ToAnotherHandler();

            logger?.LogDebug("Ignored a bounce for a mail that is no recorded support answer and that no other handler recognised.");

            return EmailHandlingOutcome.Ignored(supportCase == null ? "bounce for an unknown mail" : "bounce for a mail that is no answer");
        }

        if (!await ledger.TryRecordAsync(Source, mail.MessageId, cancellationToken)) return EmailHandlingOutcome.Ignored("already handled");

        await store.SetMessageDeliveryAsync(supportCase.TeamKey, supportCase.Id, sequence.Value, SupportMessageDelivery.Failed,
            string.IsNullOrWhiteSpace(mail.BounceReason) ? "The receiving mail server returned the answer as undeliverable." : mail.BounceReason,
            cancellationToken);

        notifier?.Notify(new SupportCaseUpdatedEventArgs
        {
            TeamKey = supportCase.TeamKey,
            CaseId = supportCase.Id,
            Change = SupportCaseChange.DeliveryChanged,
            FromChannel = true
        });

        logger?.LogWarning("The answer {Sequence} on support case {CaseId} bounced and is marked as not delivered.", sequence.Value, supportCase.Id);

        return EmailHandlingOutcome.Undelivered(supportCase.Id);
    }

    /// <summary>Offers the bounce to each registered handler in turn until one recognises the mail it names.</summary>
    /// <remarks>A handler that fails is logged and the next one is tried, so one broken handler cannot keep a bounce from another.</remarks>
    private async Task<bool> HandOverAsync(InboundMail mail, CancellationToken cancellationToken)
    {
        var bounce = new MailBounce
        {
            OriginalMessageId = mail.BouncedMessageId,
            Reason = mail.BounceReason,
            ReceivedAt = timeProvider.GetUtcNow().UtcDateTime
        };

        foreach (var handler in bounceHandlers ?? [])
        {
            try
            {
                if (await handler.HandleAsync(bounce, cancellationToken)) return true;
            }
            catch (Exception e) when (e is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                logger?.LogWarning(e, "The bounce handler {Handler} could not apply a bounce; the next handler is tried.", handler.GetType().Name);
            }
        }

        return false;
    }

    /// <summary>
    /// Opens a case from a mail that matches nothing — in the sender's team when that is certain, otherwise
    /// unassigned.
    /// </summary>
    /// <remarks>
    /// <b>Unattributed unless the sender is proven, and that is the honest record.</b> A <c>From</c> header
    /// authenticates nobody, so the address becomes the display name and
    /// <see cref="SupportCase.AuthorIdentity"/> stays null. An unverified address in the identity field would be
    /// a claim the product then trusts.
    /// <para>
    /// <b>The exception is <see cref="SupportSenderPlacement"/>'s one certain answer</b>: the transport verified
    /// the sender, one account holds the address, and it is an active member of one team. The case then opens
    /// in that team as if that member had raised it on the site — their identity, their name, the visibility a
    /// site case starts with. Every lesser match is kept on the case as suggestions for staff.
    /// </para>
    /// <para>
    /// <b>The binding carries the sender as the correspondent</b>, which is what makes the trust rule work
    /// from the second mail onwards: the case corresponds with exactly one address, so a stranger who learns
    /// the thread id still cannot write into it.
    /// </para>
    /// <para>
    /// <b>A support mailbox collects spam, and this turns some of it into cases.</b> The recipient filter
    /// narrows it, the automated-mail check drops responders and the ledger drops repeats -- but mail from a
    /// person to an address that accepts mail from anyone is a case by definition. Rate limiting is the
    /// host's, by decision.
    /// </para>
    /// </remarks>
    private async Task<EmailHandlingOutcome> RaiseAsync(InboundMail mail, SupportMailbox mailbox, CancellationToken cancellationToken)
    {
        var text = QuotedText.Trim(mail.Body, out var textCut);

        if (IsEmpty(mail, text)) return EmailHandlingOutcome.Ignored("empty body");

        var placement = SupportSenderPlacement.Decide(mail.SenderVerified, await SendersAsync(mail.From, cancellationToken));
        var team = placement.Team;
        var authorIdentity = placement.Sender?.Identity;
        var authorName = placement.Sender?.Name ?? mail.From;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var caseId = Guid.NewGuid().ToString();

        // Stored under the new case's id before the case exists, so its first message can name them.
        var files = await StoreFilesAsync(caseId, mail, mailbox.Options, cancellationToken);
        var body = Body(text, files.Notes);
        var html = Html(mail.BodyHtml, textCut, files.Notes);

        var supportCase = new SupportCase
        {
            Id = caseId,
            TeamKey = team?.Key,
            TeamName = team?.Name,
            AuthorIdentity = authorIdentity,
            AuthorName = authorName,
            Subject = string.IsNullOrWhiteSpace(mail.Subject) ? SubjectFromMessage.Derive(SubjectSource(body, files.Stored)) : mail.Subject.Trim(),
            Status = SupportCaseStatus.Open,
            CreatedAt = now,
            MessageCount = 1,
            Visibility = team == null ? null : DefaultVisibility(),
            SuggestedTeams = placement.Suggestions,
            SuggestionReason = placement.Reason,
            Bindings =
            [
                new SupportChannelBinding
                {
                    ChannelType = SupportChannelType.Email,
                    ExternalId = mail.MessageId,
                    Address = mail.From,
                    Mailbox = mailbox.Name,
                    MailboxAddress = string.IsNullOrWhiteSpace(mailbox.Options.FromAddress) ? null : mailbox.Options.FromAddress.Trim()
                }
            ]
        };

        await store.AddCaseAsync(supportCase, new SupportMessage
        {
            Sequence = 1,
            Kind = SupportMessageKind.User,
            AuthorIdentity = authorIdentity,
            AuthorName = authorName,
            Body = body,
            BodyHtml = html,
            SentAt = now,

            // It arrived through the channel, so it is already delivered. Marking it Pending would have the
            // reply path mail the customer their own message back.
            Delivery = SupportMessageDelivery.Sent,
            Source = SupportChannelType.Email,
            Attachments = files.Stored
        }, cancellationToken);

        if (team != null) await store.MarkReadAsync(team.Key, caseId, authorIdentity, 1, cancellationToken);

        notifier?.Notify(new SupportCaseUpdatedEventArgs
        {
            TeamKey = team?.Key,
            CaseId = caseId,
            Change = SupportCaseChange.Raised,
            FromChannel = true
        });

        if (team != null)
            logger?.LogInformation("Opened support case {CaseId} in team {TeamKey} from the support mailbox {Mailbox}: the mail's sender is verified and a member of that team alone.", caseId, team.Key, mailbox.Name);
        else
            logger?.LogInformation("Opened unassigned support case {CaseId} from a mail sent by {Sender} to the support mailbox {Mailbox}, suggesting {SuggestionCount} team(s).", caseId, mail.From, mailbox.Name, placement.Suggestions.Length);

        return EmailHandlingOutcome.Raised(caseId);
    }

    /// <summary>
    /// The accounts holding the sender's address, or none when there is no directory or it fails.
    /// </summary>
    /// <remarks>
    /// <b>A failure here must not lose the mail.</b> The ledger has already recorded it, so an exception would
    /// mean it is never read again; opening it unassigned instead costs only the suggestion.
    /// </remarks>
    private async Task<SupportSender[]> SendersAsync(string address, CancellationToken cancellationToken)
    {
        if (senderDirectory == null || string.IsNullOrWhiteSpace(address)) return [];

        try
        {
            return await senderDirectory.FindAsync(address, cancellationToken) ?? [];
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger?.LogWarning(e, "The sender of a support mail could not be looked up, so its case opens without a team.");

            return [];
        }
    }

    /// <summary>What a case raised on the site starts as, so a case opened in a team by mail reads no differently.</summary>
    private SupportCaseVisibility? DefaultVisibility()
        => caseOptions?.Value is { Visibility: SupportCaseVisibilityMode.PerCase } configured ? configured.DefaultVisibility : null;

    /// <summary>Mail sent from any support mailbox's address, so an answer one mailbox sent never opens a case in another.</summary>
    private bool IsOurOwn(InboundMail mail, MailOptions mailOptions)
        => SupportMailboxes.SameAddress(mail.From, mailOptions.FromAddress) || _mailboxes.IsOwnAddress(mail.From);

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
    private async Task<SupportCase> MatchAsync(InboundMail mail, MailOptions mailOptions, CancellationToken cancellationToken)
    {
        foreach (var threadId in ThreadIds(mail))
        {
            var found = await store.GetCaseByBindingAsync(SupportChannelType.Email, threadId, cancellationToken);

            if (found != null) return found;
        }

        var caseId = PerCaseAddress.CaseIdIn(mail.DeliveredTo, mailOptions.FromAddress);

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

    private bool Corresponds(SupportChannelBinding binding, string sender) => Corresponds(binding, sender, policy);

    // Outside production the answer went to the policy's override address instead, so a reply from there is the correspondent's own.
    internal static bool Corresponds(SupportChannelBinding binding, string sender, Tharga.Team.Service.Email.IOutboundMailPolicy policy)
    {
        if (string.IsNullOrWhiteSpace(binding?.Address) || string.IsNullOrWhiteSpace(sender)) return false;
        if (string.Equals(binding.Address.Trim(), sender.Trim(), StringComparison.OrdinalIgnoreCase)) return true;

        var decision = policy?.Decide(binding.Address, string.Empty);
        return decision is { Action: Tharga.Team.Service.Email.OutboundMailAction.Redirect } && string.Equals(decision.Recipient?.Trim(), sender.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Nothing written and no file carried, so there is nothing to keep.</summary>
    private static bool IsEmpty(InboundMail mail, string text)
        => string.IsNullOrWhiteSpace(text) && !mail.HadAttachments && mail.Attachments is not { Count: > 0 };

    /// <remarks>
    /// <b>Over-long mail is trimmed, not refused.</b> The service throws past
    /// <see cref="SupportCaseLimits.MaxMessageLength"/>, which is right for somebody typing into a form who
    /// can shorten it and try again. There is nobody to tell here, and the ledger has already recorded the
    /// message id -- so refusing would discard what a customer sent and never ask again. A pasted log file is
    /// a real support case, and the tail of it is the part nobody reads.
    /// </remarks>
    private static string Body(string text, IReadOnlyList<string> notes)
    {
        if (notes.Count > 0) text = $"{text}\n\n{string.Join("\n", notes)}".Trim();

        if (text.Length <= SupportCaseLimits.MaxMessageLength) return text;

        const string marker = "\n\n[trimmed]";

        return string.Concat(text.AsSpan(0, SupportCaseLimits.MaxMessageLength - marker.Length), marker);
    }

    /// <summary>
    /// The mail's formatting to keep beside its text: its HTML with the quoted thread cut off, sanitized, and the
    /// notes about files not kept added; null when there is none worth keeping.
    /// </summary>
    /// <remarks>
    /// <b>Dropped whenever it would show more than the text does.</b> When the text lost a quoted thread and no quote
    /// was found in the HTML, the HTML still carries that thread in some shape not recognised, and a transcript
    /// showing every earlier message again under each reply is the failure the text trimming exists to prevent.
    /// HTML with no formatting beyond paragraphs and line breaks, or too long to keep, is dropped too; the text is
    /// always there.
    /// </remarks>
    private static string Html(string mailHtml, bool textCut, IReadOnlyList<string> notes)
    {
        if (string.IsNullOrWhiteSpace(mailHtml)) return null;

        var (reply, htmlCut) = QuotedHtml.Trim(mailHtml);
        if (textCut && !htmlCut) return null;

        var html = SafeHtml.Sanitize(reply);
        if (html == null || !SafeHtml.IsFormatted(html)) return null;

        if (notes.Count > 0) html += $"<p>{string.Join("<br>", notes.Select(WebUtility.HtmlEncode))}</p>";

        return html.Length <= SupportCaseLimits.MaxMessageHtmlLength ? html : null;
    }

    /// <summary>What a case opened without a subject is named after: its text, or its files when it has none.</summary>
    private static string SubjectSource(string body, SupportAttachment[] files)
        => string.IsNullOrWhiteSpace(body) ? string.Join(", ", files.Select(x => x.FileName)) : body;

    /// <summary>
    /// Keeps the mail's files that fit the limits under the case, and notes in the message each one that was
    /// not kept and why.
    /// </summary>
    /// <remarks>
    /// <b>A file that cannot be stored never loses the mail.</b> The ledger has already recorded it, so a store
    /// that fails, or none registered, leaves a note where the file would have been and the rest goes on.
    /// </remarks>
    private async Task<(SupportAttachment[] Stored, IReadOnlyList<string> Notes)> StoreFilesAsync(string caseId, InboundMail mail, MailOptions mailOptions, CancellationToken cancellationToken)
    {
        if (mail.Attachments is not { Count: > 0 } attachments)
            return ([], mail.HadAttachments ? ["[attachments were not stored]"] : []);

        if (attachmentStore == null)
            return ([], [.. attachments.Select(x => $"[{x.FileName} was not stored: nowhere to keep files is configured.]")]);

        var (kept, notes) = SupportAttachmentLimitCheck.Screen(attachments, SupportAttachmentLimitCheck.LimitsOf(mailOptions));
        var stored = new List<SupportAttachment>();
        var allNotes = notes.ToList();

        foreach (var file in kept)
        {
            try
            {
                using var content = new MemoryStream(file.Content ?? [], false);

                stored.Add(await attachmentStore.SaveAsync(caseId, new SupportAttachmentUpload
                {
                    FileName = file.FileName,
                    ContentType = file.ContentType,
                    Content = content,
                    Inline = file.Inline,
                    ContentId = file.ContentId
                }, cancellationToken));
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                logger?.LogWarning(e, "A file of a support mail on case {CaseId} could not be stored.", caseId);
                allNotes.Add($"[{file.FileName} could not be stored.]");
            }
        }

        if (allNotes.Count > 0)
            logger?.LogInformation("{Count} file(s) of a support mail on case {CaseId} were not stored.", allNotes.Count, caseId);

        return ([.. stored], allNotes);
    }
}

/// <summary>What one received mail resulted in.</summary>
/// <param name="WasApplied">True when it was written to a case, whether an existing one or a new one.</param>
/// <param name="CaseId">The case it reached, or null.</param>
/// <param name="Reason">Why it was ignored, or null when it was applied.</param>
/// <param name="OpenedCase">True when it opened a case rather than continuing one.</param>
/// <param name="Bounced">True when it was a bounce that marked one of the case's answers as not delivered.</param>
/// <param name="HandedOver">True when it was a bounce for no support answer that a registered bounce handler recognised.</param>
/// <remarks>
/// Ignoring is the common outcome and is not a failure: a shared mailbox carries mail for other sites, for
/// other systems and for people. It is reported rather than thrown so the poller can log a count instead of
/// treating a busy mailbox as a series of errors.
/// </remarks>
internal readonly record struct EmailHandlingOutcome(bool WasApplied, string CaseId, string Reason, bool OpenedCase = false, bool Bounced = false, bool HandedOver = false)
{
    public static EmailHandlingOutcome Ignored(string reason) => new(false, null, reason);

    public static EmailHandlingOutcome Applied(string caseId) => new(true, caseId, null);

    public static EmailHandlingOutcome Raised(string caseId) => new(true, caseId, null, true);

    public static EmailHandlingOutcome Undelivered(string caseId) => new(true, caseId, null, Bounced: true);

    public static EmailHandlingOutcome ToAnotherHandler() => new(false, null, "bounce for mail another handler sent", HandedOver: true);
}
