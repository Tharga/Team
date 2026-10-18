using System.Globalization;
using Tharga.Team.Email;
using Tharga.Team.Service;
using Tharga.Team.Service.Html;

namespace Tharga.Team.Support.Cases;

/// <summary>
/// Support-case operations over the persistence port.
/// </summary>
/// <remarks>
/// <b>This class authorizes nothing.</b> Every check lives in
/// <see cref="AuthorizationSupportCaseServiceDecorator"/>, which wraps it — one enforcement point, so there
/// is no second place to keep in step and no path that reaches the store having skipped a check. What is
/// here is the domain: assigning identity, stamping the author, and composing the operations the store
/// applies atomically.
/// </remarks>
internal sealed class SupportCaseService(
    ISupportCaseStore store,
    TeamAuthorizer authorizer,
    TimeProvider timeProvider,
    IEnumerable<ISupportChannel> channels = null,
    ISupportCaseNotifier notifier = null,
    ISupportResponder responder = null,
    SupportCaseVisibilityMode visibilityMode = SupportCaseVisibilityMode.TeamWide,
    ITeamDirectoryService teamDirectory = null,
    IUserService userService = null,
    SupportCaseVisibility defaultVisibility = SupportCaseVisibility.Private,
    SupportCaseVisibility? assistantDefaultVisibility = null,
    ISupportAttachmentStore attachmentStore = null,
    SupportAttachmentLimits attachmentLimits = null) : ISupportCaseService, ISupportCaseTeamQueries
{
    private readonly SupportAttachmentLimits _attachmentLimits = attachmentLimits ?? SupportAttachmentLimits.Default;

    /// <remarks>
    /// <b>Several channels at once, because they face different people.</b> Email faces the customer and
    /// Slack faces support, so a case can hold a binding for each — which is what
    /// <see cref="SupportCase.Bindings"/> being an array has always meant. This took a single channel until
    /// 3.18, so configuring both silently used whichever registered first.
    /// </remarks>
    private readonly ISupportChannel[] _channels = channels?.ToArray() ?? [];

    /// <summary>The mail channel's own composition of an answer, so the route and preview shown are what posting sends.</summary>
    private ISupportAnswerMail AnswerMail => _channels.OfType<ISupportAnswerMail>().FirstOrDefault();

    public async Task<SupportCase> RaiseCaseAsync(string teamKey, string subject, string body, SupportAssistance assistance = SupportAssistance.None, CancellationToken cancellationToken = default)
    {
        RequireWithinLength(body);

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var subjectIdentity = await authorizer.GetSubjectAsync();
        var authorName = await DisplayNameAsync();

        var withAssistant = assistance == SupportAssistance.Assistant && responder != null;

        var supportCase = new SupportCase
        {
            Id = Guid.NewGuid().ToString(),
            TeamKey = teamKey,
            TeamName = await TeamNameAsync(teamKey),
            AuthorIdentity = subjectIdentity,
            AuthorName = authorName,
            Subject = string.IsNullOrWhiteSpace(subject) ? SubjectFromMessage.Derive(body) : subject.Trim(),
            Status = SupportCaseStatus.Open,
            CreatedAt = now,
            MessageCount = 1,
            AssistantState = withAssistant ? SupportAssistantState.Active : SupportAssistantState.None,
            Visibility = visibilityMode == SupportCaseVisibilityMode.PerCase
                ? withAssistant ? assistantDefaultVisibility ?? defaultVisibility : defaultVisibility
                : null
        };

        var firstMessage = new SupportMessage
        {
            Sequence = 1,
            Kind = SupportMessageKind.User,
            AuthorIdentity = subjectIdentity,
            AuthorName = authorName,
            Body = body,
            SentAt = now
        };

        await store.AddCaseAsync(supportCase, firstMessage, cancellationToken);
        await MarkOwnMessageReadAsync(teamKey, supportCase.Id, subjectIdentity, firstMessage.Sequence, cancellationToken);

        // The case is written first and is authoritative. Projecting it onto a channel comes after, so a
        // channel that is slow, misconfigured or down cannot stop somebody reporting a problem -- they get a
        // case either way, and an undelivered entry stays visible as Pending rather than being lost.
        // A conversation the assistant is answering is the product talking to itself; channels hear of it
        // when somebody asks for a person, in RequestHumanAsync.
        if (supportCase.AssistantState != SupportAssistantState.Active)
        {
            await ProjectAsync(supportCase, body, firstMessage.Sequence, cancellationToken);
        }

        Notify(teamKey, supportCase.Id, SupportCaseChange.Raised);

        return supportCase;
    }

    public async Task<bool> RunAssistantAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
    {
        if (responder == null) return false;

        var supportCase = await store.GetCaseAsync(teamKey, caseId, cancellationToken);
        if (supportCase?.AssistantState != SupportAssistantState.Active) return false;

        var transcript = await store.GetMessagesAsync(teamKey, caseId, null, SupportCaseLimits.MaxMessagesPerCase, cancellationToken);

        var answer = await responder.AnswerAsync(supportCase, transcript.Items, cancellationToken);
        if (!answer.Answered) return false;

        var message = new SupportMessage
        {
            Sequence = 0,
            Kind = SupportMessageKind.Assistant,
            AuthorName = SupportCaseActors.AssistantName,
            Body = answer.Body,
            SentAt = timeProvider.GetUtcNow().UtcDateTime
        };

        await store.AppendMessageAsync(teamKey, caseId, message, cancellationToken);

        // Not projected onto a channel. Slack is where support watches for work arriving, and an assistant
        // answering is the opposite of work arriving; email faces the customer, who is reading the case they
        // are already looking at. A case the assistant could not finish reaches support when the customer
        // replies, which is exactly when it needs to.
        Notify(teamKey, caseId, SupportCaseChange.Replied);

        return true;
    }

    /// <remarks>
    /// Asking for a person on a case that never had an assistant is a no-op rather than an error, so a
    /// component can offer the action without first working out whether it applies. The transition is
    /// one-way: <see cref="SupportAssistantState.HandedOff"/> is terminal.
    /// </remarks>
    public async Task RequestHumanAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
    {
        var supportCase = await store.GetCaseAsync(teamKey, caseId, cancellationToken)
            ?? throw new InvalidOperationException($"No case '{caseId}' on team '{teamKey}'.");

        if (supportCase.AssistantState != SupportAssistantState.Active) return;

        await store.SetAssistantStateAsync(teamKey, caseId, SupportAssistantState.HandedOff, cancellationToken);

        // A conversation that started with the assistant becomes a support case, and reads as one from now on.
        if (visibilityMode == SupportCaseVisibilityMode.PerCase
            && supportCase.Visibility == (assistantDefaultVisibility ?? defaultVisibility)
            && supportCase.Visibility != defaultVisibility)
        {
            await store.TrySetVisibilityAsync(teamKey, caseId, defaultVisibility, cancellationToken);
        }

        Notify(teamKey, caseId, SupportCaseChange.Replied);
    }

    /// <summary>
    /// What the member and the assistant said before a person was asked for, so whoever picks it up in a
    /// channel starts from the conversation rather than from its first line.
    /// </summary>
    private async Task<string> ConversationSoFarAsync(string teamKey, string caseId, CancellationToken cancellationToken)
    {
        var messages = (await store.GetMessagesAsync(teamKey, caseId, null, SupportCaseLimits.MaxMessagesPerCase, cancellationToken)).Items;

        return string.Join("\n", messages
            .Where(x => x.Kind != SupportMessageKind.System)
            .Select(x => $"{x.AuthorName}: {x.Body}"));
    }

    public Task ReplyToCaseAsync(string teamKey, string caseId, string body, CancellationToken cancellationToken = default)
        => WriteAsync(teamKey, caseId, body, null, [], asSupport: false, cancellationToken);

    public Task ReplyToCaseAsync(string teamKey, string caseId, string body, IReadOnlyList<SupportAttachmentUpload> attachments, CancellationToken cancellationToken = default)
        => WriteAsync(teamKey, caseId, body, null, attachments, asSupport: false, cancellationToken);

    public Task ReplyToCaseAsync(string teamKey, string caseId, string body, string bodyHtml, IReadOnlyList<SupportAttachmentUpload> attachments, CancellationToken cancellationToken = default)
        => WriteAsync(teamKey, caseId, body, bodyHtml, attachments, asSupport: false, cancellationToken);

    public Task AnswerCaseAsync(string teamKey, string caseId, string body, CancellationToken cancellationToken = default)
        => WriteAsync(teamKey, caseId, body, null, [], asSupport: true, cancellationToken);

    public Task AnswerCaseAsync(string teamKey, string caseId, string body, IReadOnlyList<SupportAttachmentUpload> attachments, CancellationToken cancellationToken = default)
        => WriteAsync(teamKey, caseId, body, null, attachments, asSupport: true, cancellationToken);

    public Task AnswerCaseAsync(string teamKey, string caseId, string body, string bodyHtml, IReadOnlyList<SupportAttachmentUpload> attachments, CancellationToken cancellationToken = default)
        => WriteAsync(teamKey, caseId, body, bodyHtml, attachments, asSupport: true, cancellationToken);

    /// <remarks>
    /// The entry is read by its sequence and the attachment found on it, so an id belonging to another entry or
    /// another case finds nothing even before the store compares the case it was saved for.
    /// </remarks>
    public async Task<SupportAttachmentContent> OpenAttachmentAsync(string teamKey, string caseId, int sequence, string attachmentId, CancellationToken cancellationToken = default)
    {
        if (attachmentStore == null || sequence < 1 || string.IsNullOrWhiteSpace(attachmentId)) return null;

        var page = await store.GetMessagesAsync(teamKey, caseId, (sequence - 1).ToString(CultureInfo.InvariantCulture), 1, cancellationToken);
        var attachment = page?.Items?.FirstOrDefault(x => x.Sequence == sequence)?.Attachments?.FirstOrDefault(x => x.Id == attachmentId);
        if (attachment == null) return null;

        var opened = await attachmentStore.OpenAsync(caseId, attachmentId, cancellationToken);

        return opened == null ? null : new SupportAttachmentContent { Attachment = attachment, Content = opened.Content };
    }

    public Task<SupportAttachmentLimits> GetAttachmentLimitsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(_attachmentLimits);

    public async Task<SupportMessageDelivery> ResendAsync(string teamKey, string caseId, int sequence, CancellationToken cancellationToken = default)
    {
        var supportCase = await store.GetCaseAsync(teamKey, caseId, cancellationToken)
            ?? throw new InvalidOperationException($"No case '{caseId}' on team '{teamKey}'.");

        var messages = (await store.GetMessagesAsync(teamKey, caseId, null, SupportCaseLimits.MaxMessagesPerCase, cancellationToken)).Items;
        var message = messages.FirstOrDefault(x => x.Sequence == sequence)
            ?? throw new InvalidOperationException($"Support case '{caseId}' has no entry {sequence}.");

        if (message.Delivery is not (SupportMessageDelivery.Failed or SupportMessageDelivery.Pending)) return message.Delivery;

        var status = await DeliverAsync(teamKey, caseId, supportCase, message, cancellationToken);

        Notify(teamKey, caseId, SupportCaseChange.DeliveryChanged);

        return status;
    }

    /// <remarks>The binding an answer is posted into is the first mail binding, as delivering takes it; a case with none, or a host that mails nothing, is answered on the site. A mail channel of the host's own is described by the address alone.</remarks>
    public async Task<SupportAnswerRoute> GetAnswerRouteAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
    {
        var supportCase = await store.GetCaseAsync(teamKey, caseId, cancellationToken);
        if (supportCase == null) return null;

        var binding = MailBindingOf(supportCase);

        if (binding == null) return SupportAnswerRoute.Site;

        return AnswerMail?.RouteOf(supportCase, binding) ?? new SupportAnswerRoute { ByMail = true, To = binding.Address };
    }

    /// <remarks>The draft becomes the entry answering would append — the writer's name, the next sequence, sanitized and derived as written — and is mailed nowhere.</remarks>
    public async Task<MailPreview> PreviewAnswerAsync(string teamKey, string caseId, string body, string bodyHtml, CancellationToken cancellationToken = default)
    {
        var supportCase = await store.GetCaseAsync(teamKey, caseId, cancellationToken);
        var binding = supportCase == null || AnswerMail == null ? null : MailBindingOf(supportCase);
        if (binding == null) return null;

        (body, bodyHtml) = Content(body, bodyHtml);

        var draft = new SupportMessage
        {
            Sequence = supportCase.MessageCount + 1,
            Kind = SupportMessageKind.User,
            AuthorIdentity = await authorizer.GetSubjectAsync(),
            AuthorName = await DisplayNameAsync(),
            Body = body ?? string.Empty,
            BodyHtml = bodyHtml,
            SentAt = timeProvider.GetUtcNow().UtcDateTime,
            AsSupport = true
        };

        return await AnswerMail.PreviewAsync(supportCase, binding, draft, cancellationToken);
    }

    private SupportChannelBinding MailBindingOf(SupportCase supportCase)
    {
        if (_channels.All(x => x.ChannelType != SupportChannelType.Email)) return null;

        var binding = supportCase.Bindings?.FirstOrDefault(x => x.ChannelType == SupportChannelType.Email);

        return string.IsNullOrWhiteSpace(binding?.Address) ? null : binding;
    }

    public async Task<SupportCaseUnread[]> GetMyUnreadByCaseAsync(string teamKey, CancellationToken cancellationToken = default)
    {
        var subject = await authorizer.GetSubjectAsync();

        return string.IsNullOrEmpty(subject) ? [] : await store.GetUnreadByCaseAsync(teamKey, subject, cancellationToken);
    }

    private async Task WriteAsync(string teamKey, string caseId, string body, string bodyHtml, IReadOnlyList<SupportAttachmentUpload> attachments, bool asSupport, CancellationToken cancellationToken)
    {
        (body, bodyHtml) = Content(body, bodyHtml);
        RequireWithinLength(body);

        var uploads = (attachments ?? []).Where(x => x != null).ToArray();
        RequireAttachable(uploads);

        // Costs a read before the write, and is worth it: the caller learns the case is full before typing
        // is discarded, and the limit is checked in the domain so a second adapter inherits it rather than
        // reimplementing it.
        var existing = await store.GetCaseAsync(teamKey, caseId, cancellationToken);
        if (existing != null && existing.MessageCount >= SupportCaseLimits.MaxMessagesPerCase)
            throw new InvalidOperationException(
                $"Support case '{caseId}' already holds {existing.MessageCount} messages, which is the limit of " +
                $"{SupportCaseLimits.MaxMessagesPerCase}. Raise a new case rather than extending this one.");

        if (uploads.Length > 0 && existing == null)
            throw new InvalidOperationException($"No case '{caseId}' on team '{teamKey}' to attach files to.");

        // Writing to a closed case means it is not over: it opens again rather than refusing what was written.
        if (existing?.Status == SupportCaseStatus.Closed)
        {
            await ReopenCaseAsync(teamKey, caseId, cancellationToken);
            existing = await store.GetCaseAsync(teamKey, caseId, cancellationToken);
        }

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var stored = await StoreAttachmentsAsync(caseId, uploads, cancellationToken);

        var message = new SupportMessage
        {
            Sequence = 0,
            Kind = SupportMessageKind.User,
            AuthorIdentity = await authorizer.GetSubjectAsync(),
            AuthorName = await DisplayNameAsync(),
            Body = body ?? string.Empty,
            BodyHtml = bodyHtml,
            SentAt = now,
            AsSupport = asSupport,
            Attachments = stored
        };

        try
        {
            await store.AppendMessageAsync(teamKey, caseId, message, cancellationToken);
        }
        catch
        {
            await DiscardAsync(caseId, stored);
            throw;
        }

        var sequence = (existing?.MessageCount ?? 0) + 1;

        // An answer is unread for whoever raised the case, even when the one answering raised it too.
        if (!asSupport) await MarkOwnMessageReadAsync(teamKey, caseId, message.AuthorIdentity, sequence, cancellationToken);

        // The first message to a person opens the channels, carrying the conversation with the assistant before it;
        // asking for a person without writing anything has nothing yet for support to answer.
        if (!asSupport && existing != null && existing.AssistantState != SupportAssistantState.Active && existing.Bindings is not { Length: > 0 })
        {
            await ProjectAsync(existing, await ConversationSoFarAsync(teamKey, caseId, cancellationToken), sequence, cancellationToken);
        }
        else
        {
            await DeliverAsync(teamKey, caseId, existing, message with { Sequence = sequence }, cancellationToken);
        }

        Notify(teamKey, caseId, SupportCaseChange.Replied);
    }

    /// <summary>
    /// The entry's text and formatting from what the caller wrote: HTML sanitized here whatever the caller did, and
    /// the text derived from it, so the two can never say different things.
    /// </summary>
    /// <remarks>
    /// Images go: a file written on the site travels as an attachment, so a <c>cid:</c> reference here could only
    /// point at nothing. HTML with no formatting beyond paragraphs and line breaks, or longer than
    /// <see cref="SupportCaseLimits.MaxMessageHtmlLength"/>, is kept as its text alone; HTML with nothing readable
    /// leaves <paramref name="body"/> as written.
    /// </remarks>
    internal static (string Body, string BodyHtml) Content(string body, string bodyHtml)
    {
        var html = SafeHtml.Sanitize(bodyHtml, _ => null);
        if (html == null) return (body, null);

        var text = HtmlText.ToPlainText(html);

        return SafeHtml.IsFormatted(html) && html.Length <= SupportCaseLimits.MaxMessageHtmlLength ? (text, html) : (text, null);
    }

    /// <summary>Refuses files the limits or a missing store rule out, before anything is written.</summary>
    private void RequireAttachable(IReadOnlyList<SupportAttachmentUpload> uploads)
    {
        if (uploads.Count == 0) return;

        if (attachmentStore == null)
            throw new InvalidOperationException($"Files cannot be attached: no {nameof(ISupportAttachmentStore)} is registered.");

        SupportAttachmentLimitCheck.RequireWithin(uploads, _attachmentLimits);
    }

    /// <summary>
    /// Stores each file under the case, measured as it is read, and returns what was stored; on any failure the
    /// files already stored are removed again and the failure stands.
    /// </summary>
    /// <remarks>
    /// The name is sanitized and the content type normalized here, so nothing a caller declares reaches the
    /// transcript as given.
    /// </remarks>
    private async Task<SupportAttachment[]> StoreAttachmentsAsync(string caseId, IReadOnlyList<SupportAttachmentUpload> uploads, CancellationToken cancellationToken)
    {
        if (uploads.Count == 0) return [];

        var stored = new List<SupportAttachment>();
        long total = 0;

        try
        {
            foreach (var upload in uploads)
            {
                var contentType = AttachmentContentType.Normalize(upload.ContentType);
                var fileName = AttachmentFileName.Sanitize(upload.FileName, contentType);
                var allowed = Math.Min(_attachmentLimits.MaxFileBytes, _attachmentLimits.MaxTotalBytes - total);
                var measured = new LimitedReadStream(upload.Content, allowed);

                try
                {
                    var saved = await attachmentStore.SaveAsync(caseId, new SupportAttachmentUpload
                    {
                        FileName = fileName,
                        ContentType = contentType,
                        Content = measured
                    }, cancellationToken);

                    stored.Add(saved);
                    total += saved.Size;
                }
                catch (Exception) when (measured.Exceeded)
                {
                    throw measured.BytesRead > _attachmentLimits.MaxFileBytes
                        ? SupportAttachmentLimitCheck.TooLarge(fileName, measured.BytesRead, _attachmentLimits)
                        : SupportAttachmentLimitCheck.TooLargeTogether(_attachmentLimits);
                }
            }
        }
        catch
        {
            await DiscardAsync(caseId, stored);
            throw;
        }

        return [.. stored];
    }

    /// <summary>Removes files stored for an entry that was never written; best effort, since the failure that led here is the one to report.</summary>
    private async Task DiscardAsync(string caseId, IReadOnlyList<SupportAttachment> stored)
    {
        foreach (var attachment in stored)
        {
            try
            {
                await attachmentStore.DeleteAsync(caseId, attachment.Id, CancellationToken.None);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
            }
        }
    }

    /// <remarks>
    /// Raised after the write, never before: a notification for something that then failed to persist would
    /// send a listener to read a case that does not say what it was told.
    /// </remarks>
    private void Notify(string teamKey, string caseId, SupportCaseChange change) =>
        notifier?.Notify(new SupportCaseUpdatedEventArgs
        {
            TeamKey = teamKey,
            CaseId = caseId,
            Change = change,
            FromChannel = false
        });

    /// <summary>
    /// Opens a projection on every configured channel and records whether the opening message got anywhere.
    /// </summary>
    /// <remarks>
    /// <b>Delivered means it reached at least one channel, not all of them.</b> Delivery is one field on one
    /// entry, and channels are asymmetric on purpose: a case raised on the site opens a Slack thread for
    /// support and opens nothing by mail, because the person who typed it is already looking at the site.
    /// Requiring every channel to accept would mark a perfectly delivered entry as Pending forever.
    /// <para>
    /// <b>A refusal is not a failure.</b> Every channel returning null — none configured, or all of them
    /// declining — leaves the entry Pending, which is something a retry or a reminder can act on. The case
    /// itself already stands on its own.
    /// </para>
    /// </remarks>
    private async Task ProjectAsync(SupportCase supportCase, string body, int sequence, CancellationToken cancellationToken)
    {
        if (_channels.Length == 0) return;

        var opened = false;

        foreach (var channel in _channels)
        {
            var binding = await channel.OpenAsync(supportCase, body, cancellationToken);

            if (binding == null) continue;

            await store.AddBindingAsync(supportCase.TeamKey, supportCase.Id, binding, cancellationToken);
            opened = true;
        }

        await store.SetMessageDeliveryAsync(supportCase.TeamKey, supportCase.Id, sequence,
            opened ? SupportMessageDelivery.Sent : SupportMessageDelivery.Pending, cancellationToken: cancellationToken);
    }

    /// <summary>Posts a reply into every projection the case has.</summary>
    /// <remarks>
    /// <b>Into all of them, because they face different people.</b> Support answering a case that arrived by
    /// mail has to reach the customer's inbox *and* the Slack thread support is reading, and each channel is
    /// given only a binding of its own type.
    /// <para>
    /// <b>Pending and Failed mean different things, and the difference is worth keeping.</b> No binding at
    /// all is Pending — nothing has been tried, and a projection may yet open. A binding that refused the
    /// post is Failed, which is a thing that went wrong. A reply that reached one channel and was refused by
    /// another counts as Sent: the entry is one field, and it did reach somebody.
    /// </para>
    /// <para>
    /// A Failed entry keeps the first reason a channel gave, which is the one somebody fixing it starts from.
    /// </para>
    /// </remarks>
    private async Task<SupportMessageDelivery> DeliverAsync(string teamKey, string caseId, SupportCase existing, SupportMessage message, CancellationToken cancellationToken)
    {
        if (_channels.Length == 0) return message.Delivery;

        var attempted = false;
        var delivered = false;
        string error = null;

        foreach (var channel in _channels)
        {
            var binding = existing?.Bindings?.FirstOrDefault(x => x.ChannelType == channel.ChannelType);

            if (binding == null) continue;

            attempted = true;
            var result = await channel.PostWithResultAsync(binding, message, cancellationToken);
            delivered |= result?.Delivered == true;
            if (result?.Delivered != true) error ??= result?.Error;
        }

        var status = !attempted
            ? SupportMessageDelivery.Pending
            : delivered
                ? SupportMessageDelivery.Sent
                : SupportMessageDelivery.Failed;

        await store.SetMessageDeliveryAsync(teamKey, caseId, message.Sequence, status, status == SupportMessageDelivery.Failed ? error : null, cancellationToken);

        return status;
    }

    public async Task CloseCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow().UtcDateTime;
        var subjectIdentity = await authorizer.GetSubjectAsync();
        var authorName = await DisplayNameAsync();

        // The closure goes into the transcript rather than only onto the header, so reading the conversation
        // shows how it ended. A closure with no trace of who closed it reads as a gap.
        var closure = new SupportMessage
        {
            Sequence = 0,
            Kind = SupportMessageKind.System,
            AuthorIdentity = subjectIdentity,
            AuthorName = authorName,
            Body = $"Case closed by {authorName}.",
            SentAt = now
        };

        await store.CloseCaseAsync(teamKey, caseId, now, subjectIdentity, closure, cancellationToken);

        Notify(teamKey, caseId, SupportCaseChange.Closed);
    }

    public async Task ReopenCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
    {
        var supportCase = await store.GetCaseAsync(teamKey, caseId, cancellationToken);

        // Already open is not a failure. Two people looking at the same closed case both press reopen, and
        // the second one should see an open case rather than an error about it already being open.
        if (supportCase == null || supportCase.Status != SupportCaseStatus.Closed) return;

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var authorName = await DisplayNameAsync();

        var reopen = new SupportMessage
        {
            Sequence = 0,
            Kind = SupportMessageKind.System,
            AuthorIdentity = await authorizer.GetSubjectAsync(),
            AuthorName = authorName,
            Body = $"Case reopened by {authorName}.",
            SentAt = now
        };

        await store.ReopenCaseAsync(teamKey, caseId, reopen, cancellationToken);

        Notify(teamKey, caseId, SupportCaseChange.Reopened);
    }

    public async Task<bool> AssignCaseAsync(string caseId, string teamKey, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(teamKey)) throw new ArgumentException("A case must be assigned to a team.", nameof(teamKey));

        var now = timeProvider.GetUtcNow().UtcDateTime;
        var actor = await DisplayNameAsync();

        // In the transcript, because which tenant a case belongs to is part of its history: a case that
        // changed hands and says nothing about it reads as having always been there.
        var note = new SupportMessage
        {
            Sequence = 0,
            Kind = SupportMessageKind.System,
            AuthorIdentity = await authorizer.GetSubjectAsync(),
            AuthorName = actor,
            Body = $"Assigned to team {teamKey} by {actor}.",
            SentAt = now
        };

        // Conditional in the store, so two agents triaging the same queue do not both assign it. The loser
        // is told rather than left looking at a button that did nothing -- the case is now somebody else's,
        // and a queue that silently swallows the second click is how an operator stops trusting it.
        if (!await store.TryAssignCaseAsync(caseId, teamKey, note, cancellationToken)) return false;

        Notify(teamKey, caseId, SupportCaseChange.Assigned);

        return true;
    }

    public Task<SupportCasePage> GetUnassignedCasesAsync(string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default)
        => store.GetUnassignedCasesAsync(cursor, pageSize, cancellationToken);

    public Task<SupportCasePage> GetCasesAcrossTeamsAsync(string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default)
        => store.GetCasesAcrossTeamsAsync(cursor, pageSize, cancellationToken);

    public Task<SupportCase> GetCaseAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
        => store.GetCaseAsync(teamKey, caseId, cancellationToken);

    public Task<SupportCasePage> GetCasesAsync(string teamKey, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default)
        => store.GetCasesAsync(teamKey, cursor, pageSize, cancellationToken);

    /// <remarks>
    /// <b>A caller with no subject is shown nothing, rather than everything unattributed.</b> A case raised
    /// by inbound mail has no author identity, so matching on an empty subject would list every one of them
    /// as the caller's own -- and a principal without a name identifier is a configuration a host can
    /// produce.
    /// </remarks>
    public async Task<SupportCasePage> GetMyCasesAsync(string teamKey, string cursor = null, int pageSize = 20, CancellationToken cancellationToken = default)
    {
        var subject = await authorizer.GetSubjectAsync();

        if (string.IsNullOrEmpty(subject)) return new SupportCasePage { Items = [] };

        return await store.GetCasesByAuthorAsync(teamKey, subject, cursor, pageSize, cancellationToken);
    }

    public Task<SupportMessagePage> GetMessagesAsync(string teamKey, string caseId, string cursor = null, int pageSize = 50, CancellationToken cancellationToken = default)
        => store.GetMessagesAsync(teamKey, caseId, cursor, pageSize, cancellationToken);

    public async Task MarkReadAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
    {
        var existing = await store.GetCaseAsync(teamKey, caseId, cancellationToken);
        if (existing == null) return;

        await store.MarkReadAsync(teamKey, caseId, await authorizer.GetSubjectAsync(), existing.MessageCount, cancellationToken);

        Notify(teamKey, caseId, SupportCaseChange.Read);
    }

    public async Task<SupportCaseVisibilityChoice> GetVisibilityChoiceAsync(string teamKey, string caseId, CancellationToken cancellationToken = default)
    {
        if (visibilityMode != SupportCaseVisibilityMode.PerCase) return SupportCaseVisibilityChoice.None;

        var supportCase = await store.GetCaseAsync(teamKey, caseId, cancellationToken);
        var subject = await authorizer.GetSubjectAsync();
        if (supportCase == null || string.IsNullOrEmpty(subject) || supportCase.AuthorIdentity != subject) return SupportCaseVisibilityChoice.None;

        var messages = (await store.GetMessagesAsync(teamKey, caseId, null, SupportCaseLimits.MaxMessagesPerCase, cancellationToken)).Items;

        return new SupportCaseVisibilityChoice(true, SupportCaseVisibilityRules.MayBePrivate(supportCase, messages));
    }

    public async Task<int> GetMyOpenCountAsync(string teamKey, CancellationToken cancellationToken = default)
        => await store.GetOpenCountAsync(teamKey, await authorizer.GetSubjectAsync(), cancellationToken);

    public async Task<int> GetMyAnsweredCountAsync(string teamKey, CancellationToken cancellationToken = default)
        => await store.GetAnsweredCountAsync(teamKey, await authorizer.GetSubjectAsync(), cancellationToken);

    public async Task<int> GetMyUnreadCountAsync(string teamKey, CancellationToken cancellationToken = default)
        => await store.GetUnreadCountAsync(teamKey, await authorizer.GetSubjectAsync(), cancellationToken);

    public Task<int> GetAwaitingSupportCountAsync(string teamKey, CancellationToken cancellationToken = default)
        => store.GetAwaitingSupportCountAsync(teamKey, cancellationToken);

    public Task<int> GetAwaitingSupportCountAcrossTeamsAsync(CancellationToken cancellationToken = default)
        => store.GetAwaitingSupportCountAcrossTeamsAsync(cancellationToken);

    public Task<int> GetAwaitingUnassignedCountAsync(CancellationToken cancellationToken = default)
        => store.GetAwaitingUnassignedCountAsync(cancellationToken);

    public async Task SetCaseVisibilityAsync(string teamKey, string caseId, SupportCaseVisibility visibility, CancellationToken cancellationToken = default)
    {
        if (visibilityMode != SupportCaseVisibilityMode.PerCase)
            throw new InvalidOperationException("Support cases are visible team-wide here; a case's visibility cannot be set.");

        if (visibility == SupportCaseVisibility.Private)
        {
            var supportCase = await store.GetCaseAsync(teamKey, caseId, cancellationToken);
            var messages = (await store.GetMessagesAsync(teamKey, caseId, null, SupportCaseLimits.MaxMessagesPerCase, cancellationToken)).Items;

            if (!SupportCaseVisibilityRules.MayBePrivate(supportCase, messages))
                throw new InvalidOperationException($"Others in team '{teamKey}' have written in support case {caseId}, so it cannot be made private.");
        }

        if (!await store.TrySetVisibilityAsync(teamKey, caseId, visibility, cancellationToken))
            throw new InvalidOperationException($"Support case {caseId} was not found, so its visibility cannot change.");

        Notify(teamKey, caseId, SupportCaseChange.VisibilityChanged);
    }

    public async Task<SupportCasePage> GetCasesVisibleToTeamAsync(string teamKey, string cursor, int pageSize, CancellationToken cancellationToken)
        => await store.GetCasesVisibleToTeamAsync(teamKey, await authorizer.GetSubjectAsync(), cursor, pageSize, cancellationToken);

    public async Task<int> GetAwaitingSupportCountVisibleToTeamAsync(string teamKey, CancellationToken cancellationToken)
        => await store.GetAwaitingSupportCountVisibleToTeamAsync(teamKey, await authorizer.GetSubjectAsync(), cancellationToken);

    /// <remarks>
    /// <b>Enforced here rather than in the adapter, because the limit is a contract fact.</b>
    /// <see cref="SupportCaseLimits"/> lives beside the contracts and is documented for callers, so a second
    /// storage adapter must inherit the rule rather than be trusted to reimplement it. The MongoDB adapter
    /// keeps its own check as a last-resort backstop for a caller that reaches the port directly.
    /// </remarks>
    /// <summary>
    /// The team's name, read through the author's own team list — the caller raising a case is a member, so
    /// this needs no grant of its own.
    /// </summary>
    /// <remarks>
    /// Best effort: a case is raised whether or not a name can be read, so a host without a directory, or one
    /// that fails, simply gets a case worded with the key.
    /// </remarks>
    private async Task<string> TeamNameAsync(string teamKey)
    {
        if (teamDirectory == null || string.IsNullOrEmpty(teamKey)) return null;

        try
        {
            await foreach (var team in teamDirectory.GetTeamsAsync())
            {
                if (team.Key == teamKey) return string.IsNullOrWhiteSpace(team.Name) ? null : team.Name;
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// Whoever wrote a message has read it, so it never counts as unread for them — only somebody else's answer does.
    /// </summary>
    private Task MarkOwnMessageReadAsync(string teamKey, string caseId, string identity, int sequence, CancellationToken cancellationToken)
        => string.IsNullOrEmpty(identity)
            ? Task.CompletedTask
            : store.MarkReadAsync(teamKey, caseId, identity, sequence, cancellationToken);

    /// <summary>
    /// The writer's name as the product knows them — their user record — rather than what the sign-in token
    /// calls them, which is often an e-mail address. The token's name when there is no record to read.
    /// </summary>
    private async Task<string> DisplayNameAsync()
    {
        if (userService != null)
        {
            try
            {
                var name = (await userService.GetCurrentUserAsync())?.Name;
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // A name is wording, never a reason to refuse the write; the token's name stands in.
            }
        }

        return await authorizer.GetDisplayNameAsync();
    }

    private static void RequireWithinLength(string body)
    {
        if (body != null && body.Length > SupportCaseLimits.MaxMessageLength)
            throw new InvalidOperationException(
                $"A support message is {body.Length} characters, which exceeds the limit of " +
                $"{SupportCaseLimits.MaxMessageLength}. Attach or link long content instead of pasting it.");
    }
}
