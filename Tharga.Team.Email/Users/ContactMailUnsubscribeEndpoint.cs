using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Users;

/// <summary>The unsubscribe page for a contact's link: it asks on GET and opts the address out on POST, from either of its two buttons or from a mail provider's one-click request (RFC 8058).</summary>
/// <remarks>
/// <b>Nobody here has an account</b>, so the page lists no categories and offers no sign-in: it names the category and the
/// product, says it stops only that kind of mail and how often it comes, and offers to stop that category or every mail from
/// the product to the address. A one-click request stops the category, as the link in the mail's headers names it. Served by
/// <see cref="UserMailUnsubscribeEndpoint"/> with the same headers and no script.
/// </remarks>
internal static class ContactMailUnsubscribeEndpoint
{
    /// <summary>The form field the page's buttons send.</summary>
    internal const string ScopeField = "scope";

    /// <summary>The value of <see cref="ScopeField"/> that stops the link's category.</summary>
    internal const string CategoryScope = "category";

    /// <summary>The value of <see cref="ScopeField"/> that stops every contact mail to the address.</summary>
    internal const string EveryCategoryScope = "all";

    private const string OneClickField = "List-Unsubscribe";
    private const string OneClickValue = "One-Click";

    public static async Task GetAsync(HttpContext context, ContactMailUnsubscribeLink link, UserMailText text, UserMailOptions options)
    {
        var services = context.RequestServices;
        var name = await text.NameAsync(link.Category);
        var product = ProductOf(services, options);
        var frequencies = await FrequenciesAsync(services, context.RequestAborted);

        await UserMailUnsubscribeEndpoint.WriteAsync(context, StatusCodes.Status200OK, new UserMailUnsubscribePage
        {
            Title = await text.GetAsync(ContactMailTexts.PageTitleKey, ContactMailTexts.PageTitle),
            Heading = await text.FormatAsync(UserMailTexts.PageQuestionKey, UserMailTexts.PageQuestion, name),
            Lines =
            [
                new UserMailUnsubscribeLine("scope", await text.FormatAsync(UserMailTexts.PageOnlyThisKey, UserMailTexts.PageOnlyThis, name, product)),
                new UserMailUnsubscribeLine("description", await text.DescriptionAsync(link.Category)),
                new UserMailUnsubscribeLine("frequency", await UserMailUnsubscribeEndpoint.FrequencyAsync(text, frequencies, link.Category.Key))
            ],
            Buttons =
            [
                new UserMailUnsubscribeButton(ScopeField, CategoryScope, await text.FormatAsync(UserMailTexts.PageStopKey, UserMailTexts.PageStop, name)),
                new UserMailUnsubscribeButton(ScopeField, EveryCategoryScope, await text.FormatAsync(ContactMailTexts.PageStopAllKey, ContactMailTexts.PageStopAll, product))
            ],
            Css = options.PageCss
        });
    }

    public static async Task PostAsync(HttpContext context, string token, UserMailText text, UserMailOptions options)
    {
        var services = context.RequestServices;
        var form = await FormAsync(context);
        var oneClick = string.Equals(form?[OneClickField].ToString(), OneClickValue, StringComparison.OrdinalIgnoreCase);
        var everyCategory = !oneClick && string.Equals(form?[ScopeField].ToString(), EveryCategoryScope, StringComparison.Ordinal);

        var link = await services.GetRequiredService<ContactMailUnsubscribe>()
            .UnsubscribeAsync(token, everyCategory, oneClick ? ContactMailSuppressionSource.OneClick : ContactMailSuppressionSource.Link, context.RequestAborted);

        if (link == null)
        {
            await UserMailUnsubscribeEndpoint.InvalidAsync(context, text, options);
            return;
        }

        var heading = everyCategory
            ? await text.FormatAsync(ContactMailTexts.PageDoneAllKey, ContactMailTexts.PageDoneAll, ProductOf(services, options))
            : await text.FormatAsync(UserMailTexts.PageDoneKey, UserMailTexts.PageDone, await text.NameAsync(link.Category));

        await UserMailUnsubscribeEndpoint.WriteAsync(context, StatusCodes.Status200OK, new UserMailUnsubscribePage
        {
            Title = await text.GetAsync(ContactMailTexts.PageTitleKey, ContactMailTexts.PageTitle),
            Heading = heading,
            Lines = [new UserMailUnsubscribeLine("kept", await text.GetAsync(ContactMailTexts.PageKeptKey, ContactMailTexts.PageKept))],
            Css = options.PageCss
        });
    }

    private static string ProductOf(IServiceProvider services, UserMailOptions options)
        => ContactMailSender.Product(options, services.GetService<IOptions<ContactMessageOptions>>()?.Value);

    /// <remarks>A body that is not a form, or cannot be read as one, is read as no choice: the link's category.</remarks>
    private static async Task<IFormCollection> FormAsync(HttpContext context)
    {
        if (!context.Request.HasFormContentType) return null;

        try
        {
            return await context.Request.ReadFormAsync(context.RequestAborted);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <remarks>How often is a help to the reader, never a reason the page cannot answer.</remarks>
    private static async Task<IReadOnlyList<UserMailCategoryFrequency>> FrequenciesAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var frequencies = services.GetService<ContactMailFrequencies>();
        if (frequencies == null) return [];

        try
        {
            return await frequencies.GetAsync(cancellationToken);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            services.GetService<ILogger<ContactMailUnsubscribe>>()?.LogWarning(e, "The unsubscribe page could not read how often each contact category is sent.");
            return [];
        }
    }
}
