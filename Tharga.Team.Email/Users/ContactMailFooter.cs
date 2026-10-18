using System.Net;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Users;

/// <summary>The footer every contact mail carries: who sends it, why it came, and a link that opts the address out, in both bodies.</summary>
/// <remarks>
/// <b>Always there.</b> A contact never asked for the mail, so unlike a required category of mail to users there is no
/// contact mail without a way out. The link's token names the address and the category, never a user.
/// </remarks>
internal sealed class ContactMailFooter(UserMailUnsubscribeToken tokens, IOptions<UserMailOptions> mailOptions, IOptions<ContactMessageOptions> contactOptions, UserMailText text)
{
    /// <summary>The footer for this address and category; with no address, as a preview shows it, with a link that opts nobody out.</summary>
    public async Task<UserMailFooterContent> ForAsync(string address, ContactMailCategory category)
    {
        ArgumentNullException.ThrowIfNull(category);

        var mail = mailOptions.Value;
        var contact = contactOptions.Value;
        var url = UserMailFooter.Link(mail, address == null ? UserMailFooter.PreviewToken : tokens.CreateForAddress(address, category.Key));
        var product = ContactMailSender.Product(mail, contact);
        var senderLine = string.IsNullOrWhiteSpace(contact.SenderLine) ? product : contact.SenderLine.Replace("\r\n", "\n").Trim();

        var sender = await text.FormatAsync(ContactMailTexts.FooterSenderKey, ContactMailTexts.FooterSender, senderLine);
        var reason = await text.FormatAsync(ContactMailTexts.FooterReasonKey, ContactMailTexts.FooterReason, product);
        var unsubscribe = await text.GetAsync(UserMailTexts.FooterUnsubscribeKey, UserMailTexts.FooterUnsubscribe);

        return new UserMailFooterContent(
            $"{sender}\n{reason}\n{unsubscribe}: {url}",
            $"<p>{Encode(sender).Replace("\n", "<br>")}</p><p>{Encode(reason)} <a href=\"{Encode(url)}\">{Encode(unsubscribe)}</a></p>",
            url);
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
