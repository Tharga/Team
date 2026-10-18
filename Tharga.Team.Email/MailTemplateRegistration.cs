using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Email;

/// <summary>Registers the host's HTML template for every HTML mail the toolkit sends.</summary>
public static class MailTemplateRegistration
{
    /// <summary>Sets the template support answers and mail to users are framed in, and their previews with them; call it once, in any order with the modules that send.</summary>
    /// <remarks>
    /// <see cref="MailTemplateOptions.Html"/> is trusted host markup and is not sanitized; what is placed into it is. A mail
    /// with text alone also gets an HTML part, its text escaped into paragraphs, so the frame always applies; its text
    /// alternative is unchanged. Support answers go out in <see cref="MailTemplateOptions.SupportHtml"/> when it is set.
    /// <c>services.Configure&lt;MailTemplateOptions&gt;(…)</c> sets it as well.
    /// </remarks>
    /// <exception cref="InvalidOperationException">A template has no <see cref="MailTemplateOptions.ContentPlaceholder"/>.</exception>
    public static IServiceCollection AddThargaMailTemplate(this IServiceCollection services, Action<MailTemplateOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new MailTemplateOptions();
        configure(options);
        if (!string.IsNullOrWhiteSpace(options.Html)) MailTemplate.RequireContent(options.Html);
        if (!string.IsNullOrWhiteSpace(options.SupportHtml)) MailTemplate.RequireContent(options.SupportHtml, nameof(MailTemplateOptions.SupportHtml));

        services.Configure(configure);

        return services.AddMailTemplate();
    }

    /// <summary>The renderer every sending module resolves, framing nothing until a template is set.</summary>
    internal static IServiceCollection AddMailTemplate(this IServiceCollection services)
    {
        services.AddOptions();
        services.TryAddSingleton(sp => new MailTemplate(sp.GetRequiredService<IOptions<MailTemplateOptions>>()));

        return services;
    }
}
