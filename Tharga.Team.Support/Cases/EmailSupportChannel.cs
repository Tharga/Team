using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;
using Tharga.Team.Email;
using Tharga.Team.Service.Email;
using Tharga.Team.Service.Html;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Projects a support case onto an email thread with the person who raised it.
/// </summary>
/// <remarks>
/// <b>This lives outside the <c>Email</c> namespace on purpose</b>, exactly as <see cref="SlackSupportChannel"/>
/// lives outside <c>Slack</c>. That namespace is kept free of <c>Tharga.Team.*</c> types so it can be lifted
/// into a standalone package as a file move; this class is the bridge and belongs on the support side.
/// <para>
/// <b>The thread is the opening mail's <c>Message-ID</c></b>, kept as the binding's external id and named by
/// every later reply. Mail has no thread object either, so this is the same shape as Slack's <c>ts</c>.
/// </para>
/// <para>
/// <b>Who the case corresponds with comes from the mail that created it</b>, and is stored on the binding —
/// so replying needs no lookup and no signed-in user. By the time support answers, the person at the keyboard
/// is the agent rather than the correspondent, which is why reading it from the session would be wrong.
/// </para>
/// <para>
/// <b>An answer is composed in one place</b>, so the preview a staff member opens and the mail that is sent are the
/// same mail, the host's support frame included.
/// </para>
/// <para>
/// <b>An answer names no staff member.</b> It carries no author heading, the host's signature follows it, and each
/// earlier entry it quotes written by support is attributed to support's name (<see cref="SupportAnswerFrame"/>).
/// </para>
/// </remarks>
internal sealed class EmailSupportChannel(
    IMailClient mailClient,
    ISupportCaseStore store,
    IOptions<MailOptions> options,
    ISupportAttachmentStore attachmentStore = null,
    SupportMailboxes mailboxes = null,
    ISupportMailClients mailClients = null,
    ILogger<EmailSupportChannel> logger = null,
    IOutboundMailPolicy policy = null,
    MailTemplate template = null,
    SupportAnswerOptions answers = null,
    ITextResolver texts = null,
    IUserMailOutbox mailLog = null,
    IUserService userService = null,
    TimeProvider timeProvider = null) : ISupportChannel, ISupportAnswerMail
{
    private readonly SupportMailboxes _mailboxes = mailboxes ?? new SupportMailboxes(options.Value);
    private readonly MailTemplate _template = (template ?? MailTemplate.None).Support;

    public SupportChannelType ChannelType => SupportChannelType.Email;

    /// <summary>
    /// Opens nothing. An email projection is created by mail arriving, never from the site.
    /// </summary>
    /// <remarks>
    /// <b>This returned a binding once, and it was backwards.</b> It mailed the author whenever a case was
    /// raised on the site — which is exactly the case that must be answered *on the site*, because that is
    /// where the person who raised it is looking. Email is the channel for somebody who reached us by email.
    /// <para>
    /// So the projection is created by <c>EmailEventHandler</c> when a mail arrives, carrying the sender as
    /// the correspondent. From then on <see cref="PostAsync"/> replies into it.
    /// </para>
    /// <para>
    /// Returning null is the same "not configured, quietly" answer <see cref="SlackSupportChannel"/> gives,
    /// so a case raised on the site is complete with no email binding and nothing is logged as a failure.
    /// </para>
    /// </remarks>
    public Task<SupportChannelBinding> OpenAsync(SupportCase supportCase, string openingMessage, CancellationToken cancellationToken = default)
        => Task.FromResult<SupportChannelBinding>(null);

    public async Task<bool> PostAsync(SupportChannelBinding binding, SupportMessage message, CancellationToken cancellationToken = default)
        => (await PostWithResultAsync(binding, message, cancellationToken)).Delivered;

    /// <remarks>
    /// The reason is the mail client's own, so a mail withheld by the non-production policy says so rather than
    /// reading as a transport failure.
    /// </remarks>
    public async Task<SupportChannelPostResult> PostWithResultAsync(SupportChannelBinding binding, SupportMessage message, CancellationToken cancellationToken = default)
    {
        var (option, sender) = SenderFor(binding, message);

        if (!sender.CanSend) return SupportChannelPostResult.Failed("No mail server is configured for sending.");
        if (string.IsNullOrWhiteSpace(binding?.Address)) return SupportChannelPostResult.Failed("The case has no address to mail.");

        // It arrived from this channel, so the correspondent has already read it in their own mail client.
        // Sending it back is how every message in a thread ends up appearing twice.
        if (message.Source == SupportChannelType.Email) return SupportChannelPostResult.Failed(null);

        // The subject and the case id are neither on the binding nor on the message, and the correspondent
        // reads the subject line even though their mail client threads on the headers. One read by binding
        // is the same lookup the inbound path already does.
        var supportCase = await store.GetCaseByBindingAsync(SupportChannelType.Email, binding.ExternalId, cancellationToken);

        var (files, missing) = await FilesOfAsync(supportCase, message, cancellationToken);
        if (missing != null) return SupportChannelPostResult.Failed(missing);

        var outbound = await ComposeAsync(supportCase, binding, message, option, files, cancellationToken);
        var result = await sender.SendAsync(outbound, cancellationToken);

        await LogAsync(supportCase, binding, message, option, outbound, result, cancellationToken);

        // The answer's own id joins the thread, so a reply naming only it in In-Reply-To still finds the case,
        // and a bounce naming it finds the very entry that did not arrive.
        if (result.Success && supportCase != null && !string.IsNullOrWhiteSpace(result.MessageId))
        {
            await store.AddBindingAsync(supportCase.TeamKey, supportCase.Id, new SupportChannelBinding
            {
                ChannelType = SupportChannelType.Email,
                ExternalId = result.MessageId,
                Address = binding.Address,
                MessageSequence = message.Sequence,
                Mailbox = binding.Mailbox,
                MailboxAddress = binding.MailboxAddress
            }, cancellationToken);
        }

        if (result.SenderRefused)
            return SupportChannelPostResult.Failed($"The mail server refused to send as {option.Address}. Answer again from another address, or ask whoever runs the mail server to allow sending as it.");

        return result.Success ? SupportChannelPostResult.Ok() : SupportChannelPostResult.Failed(result.Error);
    }

    /// <summary>
    /// Records the answer in the mail log, as sent or as failed, with the address it went from and the reply address it carried, who sent it and
    /// the case it belongs to, so staff looking for a sent answer find it beside every other mail. One row per answer: sending it again updates
    /// the row, so the log says what the case says. <b>A mail log that cannot be written never costs the answer</b>: it is logged and the answer stands.
    /// </summary>
    private async Task LogAsync(SupportCase supportCase, SupportChannelBinding binding, SupportMessage message, SupportSenderOption option, OutboundMail outbound, MailSendResult result, CancellationToken cancellationToken)
    {
        if (mailLog == null || supportCase == null) return;

        try
        {
            var now = (timeProvider ?? TimeProvider.System).GetUtcNow().UtcDateTime;
            var user = userService == null ? null : await userService.GetCurrentUserAsync();
            var mail = new UserMail
            {
                RecipientAddress = binding.Address,
                Category = SupportMailLog.Category,
                Subject = outbound.Subject ?? string.Empty,
                Kind = UserMailKind.SupportAnswer,
                TeamKey = supportCase.TeamKey,
                SentByUserKey = user?.Key,
                FromAddress = option.Address,
                CaseId = supportCase.Id,
                ReplyTo = outbound.ReplyTo,
                DeduplicationKey = SupportMailLog.AnswerKey(supportCase.Id, message.Sequence)
            };

            var reason = result.SenderRefused ? $"The mail server refused to send as {option.Address}." : result.Error;
            await mailLog.AddCompletedAsync(mail, result.Success ? UserMailStatus.Sent : UserMailStatus.Failed, result.Success ? null : reason, result.MessageId, binding.Address,
                new UserMailRoute(option.Address, outbound.ReplyTo), now, cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger?.LogWarning(e, "The answer on support case {CaseId} could not be recorded in the mail log; it was still sent.", supportCase.Id);
        }
    }

    public SupportAnswerRoute RouteOf(SupportCase supportCase, SupportChannelBinding binding, SupportStaffSender staff = null)
    {
        var mailbox = MailboxOf(binding);
        var allowed = SupportAnswerSenders.Allowed(Sending, mailbox, staff);
        var sender = SupportAnswerSenders.Default(allowed, mailbox, binding.MailboxAddress);
        var decision = policy?.Decide(binding.Address, Subject(supportCase));

        return new SupportAnswerRoute
        {
            ByMail = true,
            To = binding.Address,
            From = sender?.Address ?? mailbox.Options.FromAddress,
            Senders = [.. allowed.Select(x => new SupportAnswerSender { Address = x.Address, Name = x.Name, Personal = x.Personal })],
            Mailbox = sender?.Mailbox.Name ?? mailbox.Name,
            Effect = decision?.Action switch
            {
                OutboundMailAction.Redirect => MailPolicyEffect.Redirect,
                OutboundMailAction.Withhold => MailPolicyEffect.Withhold,
                _ => MailPolicyEffect.None
            },
            RedirectedTo = decision?.Action == OutboundMailAction.Redirect ? decision.Recipient : null
        };
    }

    public async Task<MailPreview> PreviewAsync(SupportCase supportCase, SupportChannelBinding binding, SupportMessage message, CancellationToken cancellationToken = default)
    {
        var option = SenderOption(binding, message);
        var mail = await ComposeAsync(supportCase, binding, message, option, [], cancellationToken);

        return new MailPreview
        {
            From = option.Formatted,
            ReplyTo = mail.ReplyTo,
            To = mail.To,
            Subject = mail.Subject,
            Html = mail.BodyHtml,
            Text = mail.Body
        };
    }

    /// <summary>The answer mail as it goes out, before the outbound policy: the thread, the signature, the conversation so far quoted, the mailbox's Reply-To, and the HTML in the host's support frame.</summary>
    private async Task<OutboundMail> ComposeAsync(SupportCase supportCase, SupportChannelBinding binding, SupportMessage message, SupportSenderOption sender, IReadOnlyList<MailAttachment> files, CancellationToken cancellationToken)
    {
        var thread = ThreadOf(supportCase, binding);
        var earlier = supportCase == null ? [] : await EarlierAsync(supportCase, message, cancellationToken);
        var frame = await SupportAnswerFrame.CreateAsync(answers, texts);
        var subject = message.Sequence == 1 && !string.IsNullOrWhiteSpace(supportCase?.Subject) ? supportCase.Subject : Subject(supportCase);
        var body = Body(message, earlier, frame);
        var mailOptions = sender.Mailbox.Options;

        // A reply never goes to a staff member's own address: it goes to the support address the customer wrote to, with the case's tracker when the mailbox uses one.
        var supportAddress = sender.Personal ? SupportAddressOf(binding) : sender.Address;
        var replyTo = await CaseReplyAddress.ForAsync(store, mailOptions, supportCase, logger, cancellationToken, supportAddress);
        if (replyTo == null && sender.Personal) replyTo = supportAddress;

        return new OutboundMail(
            To: binding.Address,
            Subject: subject,
            Body: body,
            InReplyTo: thread[^1],
            References: thread,
            ReplyTo: replyTo,
            Attachments: files,
            BodyHtml: _template.Render(subject, body, Html(message, earlier, frame, _template.IsSet)),
            From: SupportMailboxes.SameAddress(sender.Address, mailOptions.FromAddress) ? null : sender.Formatted);
    }

    /// <summary>The mailboxes an answer can be sent through: all of them when each has a client of its own, otherwise the primary alone.</summary>
    private IReadOnlyList<SupportMailbox> Sending => mailClients == null ? [_mailboxes.Primary] : _mailboxes.All;

    /// <summary>The support address replies go to when the answer is sent from a personal one: the address the customer wrote to when the mailbox may send as it, otherwise the mailbox's own.</summary>
    private string SupportAddressOf(SupportChannelBinding binding)
    {
        var mailbox = MailboxOf(binding);

        return SupportAnswerSenders.Default(SupportAnswerSenders.Allowed(Sending, mailbox, null), mailbox, binding?.MailboxAddress)?.Address ?? mailbox.Options.FromAddress;
    }

    /// <summary>
    /// The address an answer is sent from: the one recorded on the entry when the mailbox may still send as it, otherwise the address the
    /// customer wrote to or the mailbox's own. An entry nobody chose an address for — a receipt, the assistant, an answer from before
    /// this was recorded — takes the default.
    /// </summary>
    private SupportSenderOption SenderOption(SupportChannelBinding binding, SupportMessage message)
    {
        var mailbox = MailboxOf(binding);
        var allowed = SupportAnswerSenders.Allowed(Sending, mailbox, null);
        var fallback = SupportAnswerSenders.Default(allowed, mailbox, binding?.MailboxAddress)
                       ?? new SupportSenderOption(mailbox, mailbox.Options.FromAddress?.Trim(), mailbox.Options.FromName, false);
        var chosen = message?.SentFrom;

        if (string.IsNullOrWhiteSpace(chosen)) return fallback;

        var listed = allowed.FirstOrDefault(x => SupportMailboxes.SameAddress(x.Address, chosen));
        if (listed != null) return listed;

        var owner = new[] { mailbox }.Concat(Sending).FirstOrDefault(x => SupportAnswerSenders.Covers(x.Options, chosen));

        return owner == null ? fallback : new SupportSenderOption(owner, chosen.Trim(), string.IsNullOrWhiteSpace(message.AuthorName) ? null : message.AuthorName.Trim(), true);
    }

    /// <summary>The sender of an answer and the client that sends it, or the primary mailbox's with a warning when the mailbox its case came in through is no longer configured.</summary>
    private (SupportSenderOption Sender, IMailClient Client) SenderFor(SupportChannelBinding binding, SupportMessage message)
    {
        if (_mailboxes.Find(binding?.Mailbox) == null)
            logger?.LogWarning("A support answer goes out through the primary mailbox, because the mailbox {Mailbox} its case came in through is no longer configured.", binding?.Mailbox);

        var option = SenderOption(binding, message);
        var client = option.Mailbox.IsPrimary ? mailClient : mailClients.For(option.Mailbox);

        if (client != null) return (option, client);

        var primary = _mailboxes.Primary;
        var primaryOption = SupportAnswerSenders.Default(SupportAnswerSenders.Allowed([primary], primary, null), primary, binding?.MailboxAddress)
                            ?? new SupportSenderOption(primary, primary.Options.FromAddress?.Trim(), primary.Options.FromName, false);

        return (primaryOption, mailClient);
    }

    /// <summary>The mailbox an answer goes out through: the one the case came in through, or the primary when that one is no longer configured or cannot be given a client of its own.</summary>
    private SupportMailbox MailboxOf(SupportChannelBinding binding)
    {
        var mailbox = _mailboxes.Find(binding?.Mailbox);

        return mailbox == null || (!mailbox.IsPrimary && mailClients == null) ? _mailboxes.Primary : mailbox;
    }

    /// <summary>
    /// The files the entry carries, read from the store, or why they could not all be read.
    /// </summary>
    /// <remarks>
    /// <b>All or nothing.</b> An answer that says "see the attached" and arrives without it misleads the person
    /// reading it, so a file that cannot be read keeps the whole answer back as not delivered, with the reason,
    /// where support can see it and send it again.
    /// </remarks>
    private async Task<(IReadOnlyList<MailAttachment> Files, string Missing)> FilesOfAsync(SupportCase supportCase, SupportMessage message, CancellationToken cancellationToken)
    {
        if (message.Attachments is not { Length: > 0 } attachments) return ([], null);
        if (attachmentStore == null) return (null, "The answer carries files, and no attachment store is registered to read them from.");
        if (supportCase == null) return (null, "The answer carries files, and its case could not be found to read them from.");

        var files = new List<MailAttachment>();

        foreach (var attachment in attachments)
        {
            await using var opened = await attachmentStore.OpenAsync(supportCase.Id, attachment.Id, cancellationToken);
            if (opened?.Content == null) return (null, $"The file '{attachment.FileName}' could not be read from the attachment store.");

            using var buffer = new MemoryStream();
            await opened.Content.CopyToAsync(buffer, cancellationToken);
            files.Add(new MailAttachment(attachment.FileName, attachment.ContentType, buffer.ToArray(), attachment.ContentId));
        }

        return (files, null);
    }

    /// <summary>Every message id the case's mail thread has carried, oldest first: the opening mail, then each reply and answer.</summary>
    internal static IReadOnlyList<string> ThreadOf(SupportCase supportCase, SupportChannelBinding binding)
    {
        var ids = (supportCase?.Bindings ?? [])
            .Where(x => x.ChannelType == SupportChannelType.Email && !string.IsNullOrWhiteSpace(x.ExternalId))
            .Select(x => x.ExternalId)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return ids.Count == 0 ? [binding.ExternalId] : ids;
    }

    private async Task<IReadOnlyList<SupportMessage>> EarlierAsync(SupportCase supportCase, SupportMessage message, CancellationToken cancellationToken)
    {
        var page = await store.GetMessagesAsync(supportCase.TeamKey, supportCase.Id, null, MaxQuoted * 4, cancellationToken);

        return [.. (page?.Items ?? []).Where(x => x.Sequence < message.Sequence && x.Kind != SupportMessageKind.System).OrderByDescending(x => x.Sequence).Take(MaxQuoted)];
    }

    private const int MaxQuoted = 10;

    private static string Subject(SupportCase supportCase)
        => string.IsNullOrWhiteSpace(supportCase?.Subject) ? "Re: your support case" : $"Re: {supportCase.Subject}";

    /// <summary>The answer as text: the answer itself with no heading, the signature, then the conversation so far quoted.</summary>
    internal static string Body(SupportMessage message, IReadOnlyList<SupportMessage> earlier, SupportAnswerFrame frame = null)
    {
        frame ??= SupportAnswerFrame.Default;

        var parts = new List<string> { message.Body ?? string.Empty };
        if (frame.SignatureTextFor(message) is { } signature) parts.Add(signature);
        if (frame.QuoteStyle == SupportQuoteStyle.Nested)
        {
            if (earlier.Count > 0) parts.Add(string.Join("\n", QuotedNested(earlier, 0, frame)));
        }
        else
        {
            parts.AddRange(earlier.Select(x => Quoted(x, frame)));
        }

        return string.Join("\n\n", parts);
    }

    // The conversation so far, newest first, quoted the way mail clients quote, so the answer reads on its own.
    private static string Quoted(SupportMessage message, SupportAnswerFrame frame)
        => frame.Attribution(message) + "\n" + string.Join("\n", (message.Body ?? "").Split('\n').Select(x => "> " + x.TrimEnd('\r')));

    // As Gmail quotes: the latest earlier entry under its attribution, every older one inside it a level deeper.
    private static IEnumerable<string> QuotedNested(IReadOnlyList<SupportMessage> earlier, int index, SupportAnswerFrame frame)
    {
        yield return frame.Attribution(earlier[index]);

        var inner = (earlier[index].Body ?? string.Empty).Split('\n').Select(x => x.TrimEnd('\r')).ToList();
        if (index + 1 < earlier.Count)
        {
            inner.Add(string.Empty);
            inner.AddRange(QuotedNested(earlier, index + 1, frame));
        }

        foreach (var line in inner) yield return line.Length == 0 ? ">" : "> " + line;
    }

    /// <summary>
    /// The HTML version of <see cref="Body"/>, for an entry written with formatting; null for a plain entry, which is
    /// mailed as text alone as it always was, unless <paramref name="plainAsHtml"/> asks for its escaped text, as a
    /// host template does.
    /// </summary>
    /// <remarks>
    /// The signature follows the answer as a paragraph of class <c>signature</c>. Each earlier message is quoted in a
    /// <c>blockquote</c>, as its own formatting when it had some and as its escaped text otherwise: one block with the older
    /// entries nested inside it as Gmail quotes, or one block each with <see cref="SupportQuoteStyle.PerEntry"/>. Everything is
    /// sanitized again on the way out. An image keeps its <c>cid:</c> reference only when this entry carries that file,
    /// so it travels beside the HTML; an earlier message's images are named rather than sent again.
    /// </remarks>
    internal static string Html(SupportMessage message, IReadOnlyList<SupportMessage> earlier, SupportAnswerFrame frame = null, bool plainAsHtml = false)
    {
        frame ??= SupportAnswerFrame.Default;

        var own = SafeHtml.Sanitize(message.BodyHtml, contentId => Carries(message, contentId) ? HtmlImage.Keep : HtmlImage.Described(NameOf(message, contentId)));
        if (own == null && plainAsHtml) own = string.IsNullOrEmpty(message.Body) ? string.Empty : $"<p>{SafeHtml.FromPlainText(message.Body)}</p>";
        if (own == null) return null;

        var html = new StringBuilder(own);
        if (frame.SignatureHtmlFor(message) is { } signature) html.Append(signature);

        if (frame.QuoteStyle == SupportQuoteStyle.Nested)
        {
            if (earlier.Count > 0) html.Append(QuotedNestedHtml(earlier, 0, frame));
        }
        else
        {
            foreach (var quoted in earlier)
            {
                html.Append("<p>").Append(WebUtility.HtmlEncode(frame.Attribution(quoted))).Append("</p>");
                html.Append("<blockquote>").Append(QuotedBody(quoted)).Append("</blockquote>");
            }
        }

        return html.ToString();
    }

    private static string QuotedBody(SupportMessage quoted)
        => SafeHtml.Sanitize(quoted.BodyHtml, contentId => HtmlImage.Described(NameOf(quoted, contentId))) ?? SafeHtml.FromPlainText(quoted.Body);

    private const string QuoteBar = "margin:0 0 0 0.8ex;border-left:1px solid #cccccc;padding-left:1ex";

    // Marked as Gmail marks a quote, so a customer's reply that carries it back is cut off by the same rule that cuts theirs.
    private static string QuotedNestedHtml(IReadOnlyList<SupportMessage> earlier, int index, SupportAnswerFrame frame)
    {
        var html = new StringBuilder();
        html.Append("<div class=\"gmail_quote\"><div class=\"gmail_attr\">").Append(WebUtility.HtmlEncode(frame.Attribution(earlier[index]))).Append("</div>");
        html.Append("<blockquote class=\"gmail_quote\" style=\"").Append(QuoteBar).Append("\">").Append(QuotedBody(earlier[index]));
        if (index + 1 < earlier.Count) html.Append(QuotedNestedHtml(earlier, index + 1, frame));
        html.Append("</blockquote></div>");

        return html.ToString();
    }

    private static bool Carries(SupportMessage message, string contentId)
        => (message.Attachments ?? []).Any(x => string.Equals(x.ContentId, contentId, StringComparison.Ordinal));

    private static string NameOf(SupportMessage message, string contentId)
        => (message.Attachments ?? []).FirstOrDefault(x => string.Equals(x.ContentId, contentId, StringComparison.Ordinal))?.FileName;
}
