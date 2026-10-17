using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Tharga.Team.Email.Users;

/// <summary>Answers an unsubscribe link: a page that asks on GET, and the change itself on POST, from the page's button or from a mail provider's one-click request (RFC 8058).</summary>
/// <remarks>
/// <b>GET never changes anything</b>, because mail scanners and link previews fetch every link in a mail. The page is
/// plain server-rendered HTML with no script, its text through <see cref="ITextResolver"/>, and it is neither cached
/// nor indexed, nor does it pass its address on as a referrer.
/// </remarks>
internal static class UserMailUnsubscribeEndpoint
{
    internal const string TokenParameter = "token";

    public static async Task GetAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var text = new UserMailText(services.GetService<ITextResolver>());
        var category = services.GetRequiredService<UserMailUnsubscribe>().Read(Token(context));

        if (category == null)
        {
            await InvalidAsync(context, text);
            return;
        }

        var name = await text.NameAsync(category);
        await WriteAsync(context, StatusCodes.Status200OK, await text.GetAsync(UserMailTexts.PageTitleKey, UserMailTexts.PageTitle),
            await text.FormatAsync(UserMailTexts.PageQuestionKey, UserMailTexts.PageQuestion, name),
            await text.DescriptionAsync(category),
            await text.GetAsync(UserMailTexts.PageConfirmKey, UserMailTexts.PageConfirm));
    }

    public static async Task PostAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var text = new UserMailText(services.GetService<ITextResolver>());
        var category = await services.GetRequiredService<UserMailUnsubscribe>().UnsubscribeAsync(Token(context), context.RequestAborted);

        if (category == null)
        {
            await InvalidAsync(context, text);
            return;
        }

        await WriteAsync(context, StatusCodes.Status200OK, await text.GetAsync(UserMailTexts.PageTitleKey, UserMailTexts.PageTitle),
            await text.FormatAsync(UserMailTexts.PageDoneKey, UserMailTexts.PageDone, await text.NameAsync(category)),
            await text.GetAsync(UserMailTexts.PageDoneDetailKey, UserMailTexts.PageDoneDetail),
            null);
    }

    private static string Token(HttpContext context) => context.Request.Query[TokenParameter].ToString();

    private static async Task InvalidAsync(HttpContext context, UserMailText text)
        => await WriteAsync(context, StatusCodes.Status400BadRequest, await text.GetAsync(UserMailTexts.PageTitleKey, UserMailTexts.PageTitle),
            await text.GetAsync(UserMailTexts.PageInvalidKey, UserMailTexts.PageInvalid),
            await text.GetAsync(UserMailTexts.PageInvalidDetailKey, UserMailTexts.PageInvalidDetail),
            null);

    private static async Task WriteAsync(HttpContext context, int statusCode, string title, string heading, string paragraph, string button)
    {
        var response = context.Response;
        response.StatusCode = statusCode;
        response.ContentType = "text/html; charset=utf-8";
        response.Headers.CacheControl = "no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["X-Robots-Tag"] = "noindex";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";

        await response.WriteAsync(Render(CultureInfo.CurrentUICulture.Name, title, heading, paragraph, button), Encoding.UTF8, context.RequestAborted);
    }

    /// <summary>The page: a heading, a paragraph, and a button posting back to the same address when there is something to confirm.</summary>
    internal static string Render(string language, string title, string heading, string paragraph, string button)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"").Append(Encode(string.IsNullOrWhiteSpace(language) ? "en" : language)).Append("\"><head><meta charset=\"utf-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><meta name=\"robots\" content=\"noindex\">");
        html.Append("<title>").Append(Encode(title)).Append("</title><style>");
        html.Append(":root{color-scheme:light dark}body{font-family:system-ui,sans-serif;margin:0;padding:3rem 1rem;line-height:1.5}");
        html.Append("main{max-width:32rem;margin:0 auto}h1{font-size:1.4rem;margin:0 0 .75rem}");
        html.Append("button{font:inherit;padding:.5rem 1.25rem;border-radius:.375rem;border:1px solid currentColor;background:transparent;color:inherit;cursor:pointer}");
        html.Append("</style></head><body><main><h1>").Append(Encode(heading)).Append("</h1>");

        if (!string.IsNullOrWhiteSpace(paragraph)) html.Append("<p>").Append(Encode(paragraph)).Append("</p>");
        if (!string.IsNullOrWhiteSpace(button)) html.Append("<form method=\"post\"><button type=\"submit\">").Append(Encode(button)).Append("</button></form>");

        return html.Append("</main></body></html>").ToString();
    }

    private static string Encode(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
}
