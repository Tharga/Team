using System.Net;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Users;

/// <summary>The footer and headers that let a recipient stop an optional category: a sentence saying why the mail came and a link that unsubscribes, in both bodies.</summary>
internal sealed class UserMailFooter(UserMailUnsubscribeToken tokens, IOptions<UserMailOptions> options, UserMailText text)
{
    private const string TokenParameter = "token";

    /// <summary>The address that unsubscribes this user from this category.</summary>
    public string UnsubscribeUrl(string userKey, string categoryKey)
    {
        var o = options.Value;
        var path = string.IsNullOrWhiteSpace(o.UnsubscribePath) ? UserMailOptions.DefaultUnsubscribePath : o.UnsubscribePath;

        return $"{o.BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}?{TokenParameter}={Uri.EscapeDataString(tokens.Create(userKey, categoryKey))}";
    }

    /// <summary>The mail with the footer added to its bodies and its unsubscribe address set; a required category's mail unchanged.</summary>
    public async Task<OutboundMail> ApplyAsync(OutboundMail mail, string userKey, UserMailCategory category)
    {
        if (category.Required) return mail;

        var url = UnsubscribeUrl(userKey, category.Key);
        var name = await text.NameAsync(category);
        var reason = await text.FormatAsync(UserMailTexts.FooterReasonKey, UserMailTexts.FooterReason, name);
        var unsubscribe = await text.GetAsync(UserMailTexts.FooterUnsubscribeKey, UserMailTexts.FooterUnsubscribe);

        var body = $"{mail.Body?.TrimEnd()}\n\n-- \n{reason}\n{unsubscribe}: {url}";
        var html = mail.BodyHtml == null
            ? null
            : $"{mail.BodyHtml}<hr><p>{WebUtility.HtmlEncode(reason)} <a href=\"{WebUtility.HtmlEncode(url)}\">{WebUtility.HtmlEncode(unsubscribe)}</a></p>";

        return mail with { Body = body, BodyHtml = html, UnsubscribeUrl = url };
    }
}
