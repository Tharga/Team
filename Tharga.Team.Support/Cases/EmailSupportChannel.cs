using Microsoft.Extensions.Options;
using Tharga.Team.Email;

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
/// </remarks>
internal sealed class EmailSupportChannel(
    IMailClient mailClient,
    ISupportCaseStore store,
    IOptions<MailOptions> options) : ISupportChannel
{
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
    {
        if (!mailClient.CanSend || string.IsNullOrWhiteSpace(binding?.Address)) return false;

        // It arrived from this channel, so the correspondent has already read it in their own mail client.
        // Sending it back is how every message in a thread ends up appearing twice.
        if (message.Source == SupportChannelType.Email) return false;

        // The subject and the case id are neither on the binding nor on the message, and the correspondent
        // reads the subject line even though their mail client threads on the headers. One read by binding
        // is the same lookup the inbound path already does.
        var supportCase = await store.GetCaseByBindingAsync(SupportChannelType.Email, binding.ExternalId, cancellationToken);
        var thread = ThreadOf(supportCase, binding);
        var earlier = supportCase == null ? [] : await EarlierAsync(supportCase, message, cancellationToken);

        var result = await mailClient.SendAsync(
            new OutboundMail(
                To: binding.Address,
                Subject: Subject(supportCase),
                Body: Body(message, earlier),
                InReplyTo: thread[^1],
                References: thread,
                ReplyTo: ReplyToFor(supportCase?.Id)),
            cancellationToken);

        // The answer's own id joins the thread, so a reply naming only it in In-Reply-To still finds the case.
        if (result.Success && supportCase != null && !string.IsNullOrWhiteSpace(result.MessageId))
        {
            await store.AddBindingAsync(supportCase.TeamKey, supportCase.Id, new SupportChannelBinding { ChannelType = SupportChannelType.Email, ExternalId = result.MessageId, Address = binding.Address }, cancellationToken);
        }

        return result.Success;
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

    /// <remarks>
    /// A system entry is the toolkit speaking — a closure note — so attributing it to a person would be a
    /// lie in front of a customer.
    /// </remarks>
    internal static string Body(SupportMessage message, IReadOnlyList<SupportMessage> earlier)
    {
        var text = message.Kind == SupportMessageKind.System ? message.Body : $"{message.AuthorName}:\n\n{message.Body}";

        return earlier.Count == 0 ? text : text + "\n\n" + string.Join("\n\n", earlier.Select(Quoted));
    }

    // The conversation so far, newest first, quoted the way mail clients quote, so the answer reads on its own.
    private static string Quoted(SupportMessage message)
        => $"{message.SentAt:yyyy-MM-dd HH:mm} UTC, {message.AuthorName}:\n" + string.Join("\n", (message.Body ?? "").Split('\n').Select(x => "> " + x.TrimEnd('\r')));

    /// <summary>
    /// The per-case reply address, when the host has confirmed its mail server accepts plus-addressing.
    /// </summary>
    /// <remarks>
    /// Off unless <see cref="MailOptions.PerCaseReplyTo"/> says otherwise, because a server that rejects a
    /// plus-addressed local part bounces the reply back at the customer. With it off, a reply is matched on
    /// its threading headers instead.
    /// </remarks>
    private string ReplyToFor(string caseId)
        => options.Value.PerCaseReplyTo ? PerCaseAddress.Build(options.Value.FromAddress, caseId) : null;
}
