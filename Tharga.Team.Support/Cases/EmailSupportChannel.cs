using Microsoft.Extensions.Options;
using Tharga.Team.Support.Email;

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
    ISupportMailClient mailClient,
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

        var result = await mailClient.SendAsync(
            new OutboundMail(
                To: binding.Address,
                Subject: Subject(supportCase),
                Body: Body(message),
                InReplyTo: binding.ExternalId,
                References: [binding.ExternalId],
                ReplyTo: ReplyToFor(supportCase?.Id)),
            cancellationToken);

        return result.Success;
    }

    private static string Subject(SupportCase supportCase)
        => string.IsNullOrWhiteSpace(supportCase?.Subject) ? "Re: your support case" : $"Re: {supportCase.Subject}";

    /// <remarks>
    /// A system entry is the toolkit speaking — a closure note — so attributing it to a person would be a
    /// lie in front of a customer.
    /// </remarks>
    private static string Body(SupportMessage message)
        => message.Kind == SupportMessageKind.System ? message.Body : $"{message.AuthorName}:\n\n{message.Body}";

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
