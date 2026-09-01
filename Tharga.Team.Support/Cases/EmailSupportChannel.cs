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
/// <b>Who the case corresponds with is resolved once, when the projection opens</b>, and stored on the
/// binding. It is the author's own address, read from the signed-in user rather than looked up by identity —
/// that lookup needs <c>users:manage</c>, which somebody raising a case has no reason to hold. Asking later
/// would answer the wrong question anyway: by the time support replies, the signed-in user is the agent.
/// </para>
/// </remarks>
internal sealed class EmailSupportChannel(
    ISupportMailClient mailClient,
    ISupportCaseStore store,
    IUserService userService,
    IOptions<MailOptions> options) : ISupportChannel
{
    public SupportChannelType ChannelType => SupportChannelType.Email;

    public async Task<SupportChannelBinding> OpenAsync(SupportCase supportCase, string openingMessage, CancellationToken cancellationToken = default)
    {
        if (!mailClient.CanSend) return null;

        var user = await userService.GetCurrentUserAsync();
        var correspondent = user?.EMail;

        // Nobody to write to is an ordinary state rather than a failure — a service account, or a directory
        // record carrying no mail. The case is raised regardless and simply stays on the site.
        if (string.IsNullOrWhiteSpace(correspondent)) return null;

        var result = await mailClient.SendAsync(
            new OutboundMail(
                To: correspondent,
                Subject: supportCase.Subject,
                Body: openingMessage,
                ReplyTo: ReplyToFor(supportCase.Id)),
            cancellationToken);

        if (!result.Success) return null;

        return new SupportChannelBinding
        {
            ChannelType = SupportChannelType.Email,
            ExternalId = result.MessageId,
            Address = correspondent
        };
    }

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
