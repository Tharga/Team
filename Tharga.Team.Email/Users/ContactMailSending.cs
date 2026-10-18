using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Users;

/// <summary>What the user mail dispatcher needs to send a contact mail: its categories, the opt-outs, its footer, and a transport that sends from the contact sender.</summary>
/// <remarks>Registered by <c>AddThargaContactMessages</c>; without it, a contact mail fails with the reason rather than going out in another's name.</remarks>
internal sealed class ContactMailSending(
    ContactMailCategoryRegistry categories,
    IContactMailSuppressionStore suppressions,
    ContactMailFooter footer,
    IMailClient mail,
    IOptions<UserMailOptions> mailOptions,
    IOptions<ContactMessageOptions> contactOptions)
{
    /// <summary>The categories contact mail may be sent in.</summary>
    public ContactMailCategoryRegistry Categories => categories;

    /// <summary>The footer every contact mail carries.</summary>
    public ContactMailFooter Footer => footer;

    /// <summary>The transport, sending from the contact sender.</summary>
    public IMailClient Mail => mail;

    /// <summary>Where replies go, or null for the sending address.</summary>
    public string ReplyTo => ContactMailSender.ReplyTo(mailOptions.Value, contactOptions.Value);

    /// <summary>The opt-out that stops mail in this category to this address — one of every category first — or null when there is none.</summary>
    public async Task<ContactMailSuppression> SuppressionOfAsync(string address, string categoryKey, CancellationToken cancellationToken)
        => (await suppressions.GetAsync([address], cancellationToken) ?? [])
            .Where(x => x != null && x.AppliesTo(categoryKey))
            .OrderBy(x => x.Category != null)
            .FirstOrDefault();
}
