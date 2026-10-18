using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Users;

/// <summary>Answers an unsubscribe link: a page that asks on GET, and the change itself on POST, from the page's button or from a mail provider's one-click request (RFC 8058).</summary>
/// <remarks>
/// <b>GET never changes anything</b>, because mail scanners and link previews fetch every link in a mail. The page is
/// plain server-rendered HTML with no script, its text through <see cref="ITextResolver"/>, and it is neither cached
/// nor indexed, nor does it pass its address on as a referrer.
/// <para>
/// <b>The token reveals its own category and nothing else.</b> Every category and its state is listed only for a
/// request signed in as the very user the link is for; anyone else is offered a link to sign in. A contact's link, which
/// names an address rather than a user, gets a page of its own with neither.
/// </para>
/// </remarks>
internal static class UserMailUnsubscribeEndpoint
{
    internal const string TokenParameter = "token";
    internal const string ReturnUrlParameter = "returnUrl";

    public static async Task GetAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var text = new UserMailText(services.GetService<ITextResolver>());
        var options = OptionsOf(services);
        var token = Token(context);
        var link = services.GetRequiredService<UserMailUnsubscribe>().Read(token);

        if (link == null)
        {
            if (services.GetService<ContactMailUnsubscribe>()?.Read(token) is { } contact) await ContactMailUnsubscribeEndpoint.GetAsync(context, contact, text, options);
            else await InvalidAsync(context, text, options);
            return;
        }

        var name = await text.NameAsync(link.Category);
        var frequencies = await FrequenciesAsync(services, context.RequestAborted);

        await WriteAsync(context, StatusCodes.Status200OK, await TheRestAsync(context, link, text, options, frequencies, new UserMailUnsubscribePage
        {
            Title = await text.GetAsync(UserMailTexts.PageTitleKey, UserMailTexts.PageTitle),
            Heading = await text.FormatAsync(UserMailTexts.PageQuestionKey, UserMailTexts.PageQuestion, name),
            Lines =
            [
                new UserMailUnsubscribeLine("scope", await text.FormatAsync(UserMailTexts.PageOnlyThisKey, UserMailTexts.PageOnlyThis, name, ProductOf(options))),
                new UserMailUnsubscribeLine("description", await text.DescriptionAsync(link.Category)),
                new UserMailUnsubscribeLine("frequency", await FrequencyAsync(text, frequencies, link.Category.Key))
            ],
            Button = await text.FormatAsync(UserMailTexts.PageStopKey, UserMailTexts.PageStop, name),
            Css = options.PageCss
        }));
    }

    public static async Task PostAsync(HttpContext context)
    {
        var services = context.RequestServices;
        var text = new UserMailText(services.GetService<ITextResolver>());
        var options = OptionsOf(services);
        var token = Token(context);
        var link = await services.GetRequiredService<UserMailUnsubscribe>().UnsubscribeAsync(token, context.RequestAborted);

        if (link == null)
        {
            if (services.GetService<ContactMailUnsubscribe>()?.Read(token) != null) await ContactMailUnsubscribeEndpoint.PostAsync(context, token, text, options);
            else await InvalidAsync(context, text, options);
            return;
        }

        var frequencies = await FrequenciesAsync(services, context.RequestAborted);

        await WriteAsync(context, StatusCodes.Status200OK, await TheRestAsync(context, link, text, options, frequencies, new UserMailUnsubscribePage
        {
            Title = await text.GetAsync(UserMailTexts.PageTitleKey, UserMailTexts.PageTitle),
            Heading = await text.FormatAsync(UserMailTexts.PageDoneKey, UserMailTexts.PageDone, await text.NameAsync(link.Category)),
            Lines = [new UserMailUnsubscribeLine("turn-back-on", await text.GetAsync(UserMailTexts.PageTurnBackOnKey, UserMailTexts.PageTurnBackOn))],
            Css = options.PageCss
        }));
    }

    private static string Token(HttpContext context) => context.Request.Query[TokenParameter].ToString();

    private static UserMailOptions OptionsOf(IServiceProvider services) => services.GetService<IOptions<UserMailOptions>>()?.Value ?? new UserMailOptions();

    private static string ProductOf(UserMailOptions options)
        => new[] { options.ProductName, options.Mail?.FromName, options.Mail?.FromAddress }.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))?.Trim() ?? string.Empty;

    internal static async Task InvalidAsync(HttpContext context, UserMailText text, UserMailOptions options)
        => await WriteAsync(context, StatusCodes.Status400BadRequest, new UserMailUnsubscribePage
        {
            Title = await text.GetAsync(UserMailTexts.PageTitleKey, UserMailTexts.PageTitle),
            Heading = await text.GetAsync(UserMailTexts.PageInvalidKey, UserMailTexts.PageInvalid),
            Lines = [new UserMailUnsubscribeLine("detail", await text.GetAsync(UserMailTexts.PageInvalidDetailKey, UserMailTexts.PageInvalidDetail))],
            Css = options.PageCss
        });

    /// <summary>Below the answer: every category and its state for the signed-in user the link is for, or a link to sign in for anyone else.</summary>
    private static async Task<UserMailUnsubscribePage> TheRestAsync(HttpContext context, UserMailUnsubscribeLink link, UserMailText text, UserMailOptions options, IReadOnlyList<UserMailCategoryFrequency> frequencies, UserMailUnsubscribePage page)
    {
        if (await IsTheLinksUserAsync(context, link.UserKey))
        {
            var choices = await context.RequestServices.GetRequiredService<UserMailPreferences>().GetChoicesAsync(link.UserKey, context.RequestAborted);
            var terms = string.IsNullOrWhiteSpace(options.TermsUrl) ? null : options.TermsUrl.Trim();
            var items = new List<UserMailUnsubscribeItem>();

            foreach (var choice in choices)
            {
                var category = choice.Category;
                var (state, stateText) = category.Required
                    ? ("required", await text.GetAsync(UserMailTexts.AlwaysSentKey, UserMailTexts.AlwaysSent))
                    : choice.Receives
                        ? ("on", await text.GetAsync(UserMailTexts.PageOnKey, UserMailTexts.PageOn))
                        : ("off", await text.GetAsync(UserMailTexts.PageOffKey, UserMailTexts.PageOff));

                items.Add(new UserMailUnsubscribeItem(category.Key, await text.NameAsync(category), state, stateText, category.Required ? terms : null, await FrequencyAsync(text, frequencies, category.Key)));
            }

            var settings = string.IsNullOrWhiteSpace(options.PreferencesPath)
                ? null
                : new UserMailUnsubscribeAnchor(await text.GetAsync(UserMailTexts.PageSettingsKey, UserMailTexts.PageSettings), options.PreferencesPath.Trim());

            return page with { Overview = new UserMailUnsubscribeOverview(await text.GetAsync(UserMailTexts.PageYourMailKey, UserMailTexts.PageYourMail), items, settings) };
        }

        if (string.IsNullOrWhiteSpace(options.SignInPath)) return page;

        return page with { SignIn = new UserMailUnsubscribeAnchor(await text.GetAsync(UserMailTexts.PageSignInKey, UserMailTexts.PageSignIn), SignInUrl(options)) };
    }

    /// <summary>The sign-in path, with the mail settings to return to when the host has them.</summary>
    internal static string SignInUrl(UserMailOptions options)
    {
        var signIn = options.SignInPath.Trim();
        if (string.IsNullOrWhiteSpace(options.PreferencesPath)) return signIn;

        return $"{signIn}{(signIn.Contains('?') ? '&' : '?')}{ReturnUrlParameter}={Uri.EscapeDataString(options.PreferencesPath.Trim())}";
    }

    /// <summary>Whether the request is signed in as the user the link is for, compared by the user key the toolkit resolves for its principal.</summary>
    /// <remarks>A failure to resolve the signed-in user is logged and read as not signed in, so the page still answers.</remarks>
    private static async Task<bool> IsTheLinksUserAsync(HttpContext context, string userKey)
    {
        if (context.User?.Identity?.IsAuthenticated != true) return false;

        var users = context.RequestServices.GetService<IUserService>();
        if (users == null) return false;

        try
        {
            var user = await users.GetCurrentUserAsync(context.User);
            return user != null && string.Equals(user.Key, userKey, StringComparison.Ordinal);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            context.RequestServices.GetService<ILogger<UserMailUnsubscribe>>()?.LogWarning(e, "The unsubscribe page could not resolve the signed-in user, so it offers sign-in instead of listing the categories.");
            return false;
        }
    }

    /// <remarks>How often is a help to the reader, never a reason the page cannot answer.</remarks>
    private static async Task<IReadOnlyList<UserMailCategoryFrequency>> FrequenciesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var frequencies = services.GetService<UserMailFrequencies>();
        if (frequencies == null) return [];

        try
        {
            return await frequencies.GetAsync(cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            services.GetService<ILogger<UserMailUnsubscribe>>()?.LogWarning(e, "The unsubscribe page could not read how often each category is sent.");
            return [];
        }
    }

    internal static async Task<string> FrequencyAsync(UserMailText text, IReadOnlyList<UserMailCategoryFrequency> frequencies, string categoryKey)
    {
        var phrase = frequencies.FirstOrDefault(x => x.Category == categoryKey)?.Phrase;

        return phrase == null ? null : phrase.Format(await text.GetAsync(phrase.Key, phrase.English));
    }

    internal static async Task WriteAsync(HttpContext context, int statusCode, UserMailUnsubscribePage page)
    {
        var response = context.Response;
        response.StatusCode = statusCode;
        response.ContentType = "text/html; charset=utf-8";
        response.Headers.CacheControl = "no-store";
        response.Headers["Referrer-Policy"] = "no-referrer";
        response.Headers["X-Robots-Tag"] = "noindex";
        response.Headers["X-Content-Type-Options"] = "nosniff";
        response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; form-action 'self'; frame-ancestors 'none'; base-uri 'none'";

        await response.WriteAsync(Render(page with { Language = CultureInfo.CurrentUICulture.Name }), Encoding.UTF8, context.RequestAborted);
    }

    /// <summary>The page: a heading, its lines, a button posting back to the same address when there is something to confirm, then every category for the link's own user or a link to sign in.</summary>
    internal static string Render(UserMailUnsubscribePage page)
    {
        var html = new StringBuilder();
        html.Append("<!doctype html><html lang=\"").Append(Encode(string.IsNullOrWhiteSpace(page.Language) ? "en" : page.Language)).Append("\"><head><meta charset=\"utf-8\">");
        html.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><meta name=\"robots\" content=\"noindex\">");
        html.Append("<title>").Append(Encode(page.Title)).Append("</title><style>");
        html.Append(":root{color-scheme:light dark}body{font-family:system-ui,sans-serif;margin:0;padding:3rem 1rem;line-height:1.5}");
        html.Append("main{max-width:32rem;margin:0 auto}h1{font-size:1.4rem;margin:0 0 .75rem}h2{font-size:1.1rem;margin:0 0 .5rem}p{margin:0 0 .75rem}");
        html.Append(".description,.frequency,.state{opacity:.8}.mail{margin-top:2rem}.mail ul{list-style:none;padding:0;margin:0 0 .75rem}");
        html.Append(".mail li{padding:.5rem 0;border-top:1px solid rgba(127,127,127,.3)}.mail .name{display:block;font-weight:600}.mail .state,.mail .frequency{display:block;font-size:.9rem}.sign-in{margin-top:2rem}");
        html.Append("button{font:inherit;padding:.5rem 1.25rem;border-radius:.375rem;border:1px solid currentColor;background:transparent;color:inherit;cursor:pointer}");
        if (!string.IsNullOrWhiteSpace(page.Css)) html.Append(page.Css.Replace("</", string.Empty, StringComparison.Ordinal));
        html.Append("</style></head><body><main><h1>").Append(Encode(page.Heading)).Append("</h1>");

        foreach (var line in page.Lines.Where(x => !string.IsNullOrWhiteSpace(x.Text)))
            html.Append("<p class=\"").Append(Encode(line.Class)).Append("\">").Append(Encode(line.Text)).Append("</p>");

        if (page.Buttons.Count > 0)
        {
            html.Append("<form method=\"post\">");
            foreach (var button in page.Buttons)
                html.Append("<button type=\"submit\" name=\"").Append(Encode(button.Name)).Append("\" value=\"").Append(Encode(button.Value)).Append("\">").Append(Encode(button.Text)).Append("</button>");
            html.Append("</form>");
        }
        else if (!string.IsNullOrWhiteSpace(page.Button)) html.Append("<form method=\"post\"><button type=\"submit\">").Append(Encode(page.Button)).Append("</button></form>");

        if (page.Overview is { } overview)
        {
            html.Append("<section class=\"mail\"><h2>").Append(Encode(overview.Heading)).Append("</h2><ul>");

            foreach (var item in overview.Items)
            {
                html.Append("<li data-category=\"").Append(Encode(item.Key)).Append("\" data-state=\"").Append(Encode(item.State)).Append("\">");
                html.Append("<span class=\"name\">").Append(Encode(item.Name)).Append("</span><span class=\"state\">");
                html.Append(item.StateHref == null ? Encode(item.StateText) : Anchor(item.StateText, item.StateHref)).Append("</span>");
                if (!string.IsNullOrWhiteSpace(item.Frequency)) html.Append("<span class=\"frequency\">").Append(Encode(item.Frequency)).Append("</span>");
                html.Append("</li>");
            }

            html.Append("</ul>");
            if (overview.Settings is { } settings) html.Append("<p class=\"settings\">").Append(Anchor(settings.Text, settings.Href)).Append("</p>");
            html.Append("</section>");
        }

        if (page.SignIn is { } signIn) html.Append("<p class=\"sign-in\">").Append(Anchor(signIn.Text, signIn.Href)).Append("</p>");

        return html.Append("</main></body></html>").ToString();
    }

    private static string Anchor(string text, string href) => $"<a href=\"{Encode(href)}\">{Encode(text)}</a>";

    private static string Encode(string value) => WebUtility.HtmlEncode(value ?? string.Empty);
}

/// <summary>What the unsubscribe page shows.</summary>
internal sealed record UserMailUnsubscribePage
{
    public string Language { get; init; }
    public string Title { get; init; }
    public string Heading { get; init; }
    public IReadOnlyList<UserMailUnsubscribeLine> Lines { get; init; } = [];
    public string Button { get; init; }
    public IReadOnlyList<UserMailUnsubscribeButton> Buttons { get; init; } = [];
    public UserMailUnsubscribeOverview Overview { get; init; }
    public UserMailUnsubscribeAnchor SignIn { get; init; }
    public string Css { get; init; }
}

/// <summary>One paragraph under the heading, with the class a host's style rules may address.</summary>
internal sealed record UserMailUnsubscribeLine(string Class, string Text);

/// <summary>Every category and its state, for the signed-in user the link is for.</summary>
internal sealed record UserMailUnsubscribeOverview(string Heading, IReadOnlyList<UserMailUnsubscribeItem> Items, UserMailUnsubscribeAnchor Settings);

/// <summary>One category in the overview: its name, whether it is on, off or always sent, and how often it is sent.</summary>
internal sealed record UserMailUnsubscribeItem(string Key, string Name, string State, string StateText, string StateHref, string Frequency);

/// <summary>A link on the page.</summary>
internal sealed record UserMailUnsubscribeAnchor(string Text, string Href);

/// <summary>One of several buttons posting the page's form, each sending its own name and value.</summary>
internal sealed record UserMailUnsubscribeButton(string Name, string Value, string Text);
