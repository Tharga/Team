namespace Tharga.Team.Support.Cases;

/// <summary>What support's answer to a mailed case looks like before it is sent: where it goes and the mail itself, built as sending builds it.</summary>
internal interface ISupportAnswerMail
{
    /// <summary>
    /// The address the case's mail thread answers to, the addresses the answerer may send from and the one it goes from unless they pick another,
    /// and the outbound policy's effect. <paramref name="staff"/> is the answering staff member when the host lets their role send from their own address.
    /// </summary>
    SupportAnswerRoute RouteOf(SupportCase supportCase, SupportChannelBinding binding, SupportStaffSender staff = null);

    /// <summary>The answer mail for <paramref name="message"/> as the correspondent would receive it, its files left out.</summary>
    Task<MailPreview> PreviewAsync(SupportCase supportCase, SupportChannelBinding binding, SupportMessage message, CancellationToken cancellationToken = default);
}
