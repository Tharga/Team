using System.Net;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Users;

/// <summary>The footer and headers that let a recipient stop an optional category: a sentence saying why the mail came and a link that unsubscribes, in both bodies.</summary>
internal sealed class UserMailFooter(UserMailUnsubscribeToken tokens, IOptions<UserMailOptions> options, UserMailText text)
{
    private const string TokenParameter = "token";

    /// <summary>The token a preview's unsubscribe link carries in place of a recipient's, which unsubscribes nobody.</summary>
    internal const string PreviewToken = "preview";

    /// <summary>The address that unsubscribes this user from this category.</summary>
    public string UnsubscribeUrl(string userKey, string categoryKey)
        => Url(tokens.Create(userKey, categoryKey));

    /// <summary>The footer an optional category's mail carries for this user, or null for a required category; with no user, as a preview shows it, with a link that unsubscribes nobody.</summary>
    public async Task<UserMailFooterContent> ForAsync(string userKey, UserMailCategory category)
    {
        if (category.Required) return null;

        var url = userKey == null ? Url(PreviewToken) : UnsubscribeUrl(userKey, category.Key);
        var name = await text.NameAsync(category);
        var reason = await text.FormatAsync(UserMailTexts.FooterReasonKey, UserMailTexts.FooterReason, name);
        var unsubscribe = await text.GetAsync(UserMailTexts.FooterUnsubscribeKey, UserMailTexts.FooterUnsubscribe);

        return new UserMailFooterContent(
            $"{reason}\n{unsubscribe}: {url}",
            $"<p>{WebUtility.HtmlEncode(reason)} <a href=\"{WebUtility.HtmlEncode(url)}\">{WebUtility.HtmlEncode(unsubscribe)}</a></p>",
            url);
    }

    private string Url(string token)
    {
        var o = options.Value;
        var path = string.IsNullOrWhiteSpace(o.UnsubscribePath) ? UserMailOptions.DefaultUnsubscribePath : o.UnsubscribePath;

        return $"{o.BaseUrl.TrimEnd('/')}/{path.TrimStart('/')}?{TokenParameter}={Uri.EscapeDataString(token)}";
    }
}

/// <summary>An optional category's footer: its text, its HTML, and the address that unsubscribes.</summary>
internal sealed record UserMailFooterContent(string Text, string Html, string UnsubscribeUrl);
