using System.Net;
using AngleSharp.Css.Dom;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using Ganss.Xss;

namespace Tharga.Team.Service.Html;

/// <summary>The one HTML sanitizer, run on every way in and at every render: an allow-list of formatting, web and mail links only, and images only by <c>cid:</c> reference or as data the caller read.</summary>
public static class SafeHtml
{
    private static readonly HashSet<string> Tags = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "br", "div", "h1", "h2", "h3", "h4",
        "b", "strong", "i", "em", "u", "s", "strike", "del",
        "ul", "ol", "li", "blockquote", "pre", "code", "a", "img",
        "table", "caption", "thead", "tbody", "tfoot", "tr", "th", "td"
    };

    private static readonly HashSet<string> Attributes = new(StringComparer.OrdinalIgnoreCase) { "href", "src", "alt", "title", "colspan", "rowspan", "start" };

    private static readonly HashSet<string> Schemes = new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto", "cid" };

    private static readonly HashSet<string> UriAttributes = new(StringComparer.OrdinalIgnoreCase) { "href", "src" };

    private static readonly HashSet<string> LinkSchemes = new(StringComparer.OrdinalIgnoreCase) { "http", "https", "mailto" };

    private static readonly HashSet<string> Plain = new(StringComparer.OrdinalIgnoreCase) { "p", "div", "br" };

    private static readonly string[] ImageMediaTypes = ["image/png", "image/jpeg", "image/gif", "image/webp"];

    private const string RemovedWhole = "script, style, head, title, meta, link, base, template, noscript, iframe, frame, frameset, object, embed, applet, svg, math, form, input, button, select, textarea, option, optgroup, datalist, output, audio, video, source, track, picture, canvas, map, area, dialog, [hidden]";

    private const string BlocksKeptAsLines = "h5, h6, section, article, header, footer, main, nav, aside, address, figure, figcaption, center, dl, dt, dd, details, summary, fieldset, legend";

    private const string CidScheme = "cid:";

    /// <summary>The image types an image may be shown as, because a browser decodes them without running anything.</summary>
    public static IReadOnlyList<string> ImageTypes => ImageMediaTypes;

    /// <summary>Sanitizes HTML to the allow-list, keeping <c>cid:</c> image references; null when nothing readable remains.</summary>
    public static string Sanitize(string html) => Sanitize(html, null);

    /// <summary>Sanitizes HTML to the allow-list and decides each <c>cid:</c> image through <paramref name="images"/>, where null replaces the image by its alt text; null when nothing readable remains.</summary>
    public static string Sanitize(string html, Func<string, HtmlImage> images)
    {
        var body = SanitizedBody(html, images);

        return body == null || !HasContent(body) ? null : body.InnerHtml.Trim();
    }

    /// <summary>Whether HTML holds nothing readable once sanitized, as an editor holding only empty paragraphs does.</summary>
    public static bool IsEmpty(string html) => Sanitize(html) == null;

    /// <summary>Whether sanitized HTML carries anything beyond paragraphs and line breaks, which its plain text would lose.</summary>
    public static bool IsFormatted(string html)
    {
        var body = SanitizedBody(html, null);

        return body != null && HasContent(body) && body.QuerySelectorAll("*").Any(x => !Plain.Contains(x.LocalName));
    }

    /// <summary>The Content-IDs the HTML's images name with <c>cid:</c>, in order and without repeats.</summary>
    public static IReadOnlyList<string> ContentIds(string html)
    {
        if (string.IsNullOrWhiteSpace(html)) return [];

        return [.. Parse(html).QuerySelectorAll("img[src]")
            .Select(x => ContentIdOf(x.GetAttribute("src")))
            .Where(x => x != null)
            .Distinct(StringComparer.Ordinal)];
    }

    /// <summary>Plain text as HTML: every character escaped and every line break kept.</summary>
    public static string FromPlainText(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        return string.Join("<br>", text.Replace("\r\n", "\n").Split('\n').Select(WebUtility.HtmlEncode));
    }

    /// <summary>The media type an image of this declared type is shown as, or null when it is not one of <see cref="ImageTypes"/>.</summary>
    public static string ImageMediaType(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;

        var separator = contentType.IndexOf(';');
        var mediaType = (separator < 0 ? contentType : contentType[..separator]).Trim().ToLowerInvariant();

        return ImageMediaTypes.Contains(mediaType) ? mediaType : null;
    }

    private static IHtmlElement SanitizedBody(string html, Func<string, HtmlImage> images)
    {
        if (string.IsNullOrWhiteSpace(html)) return null;

        var document = new HtmlParser().ParseDocument(html);
        var body = document.Body;
        if (body == null) return null;

        foreach (var element in body.QuerySelectorAll(RemovedWhole).ToArray()) element.Remove();
        foreach (var element in body.QuerySelectorAll("[style]").Where(IsHiddenByStyle).ToArray()) element.Remove();
        foreach (var element in body.QuerySelectorAll(BlocksKeptAsLines).ToArray()) Rename(document, element, "div");

        Sanitizer().SanitizeDom(document, body);

        FinishLinks(body);
        FinishImages(document, body, images);

        return body;
    }

    private static HtmlSanitizer Sanitizer() => new(new HtmlSanitizerOptions
    {
        AllowedTags = Tags,
        AllowedAttributes = Attributes,
        AllowedSchemes = Schemes,
        UriAttributes = UriAttributes,
        AllowedCssProperties = new HashSet<string>(),
        AllowedAtRules = new HashSet<CssRuleType>(),
        AllowedCssClasses = new HashSet<string>()
    })
    {
        KeepChildNodes = true
    };

    private static IHtmlDocument Parse(string html) => new HtmlParser().ParseDocument(html);

    /// <remarks>Hidden text in a mail — a preheader, a tracking block — would become visible once its style is gone, so it goes with the style.</remarks>
    private static bool IsHiddenByStyle(IElement element)
    {
        var style = new string((element.GetAttribute("style") ?? string.Empty).Where(x => !char.IsWhiteSpace(x)).ToArray()).ToLowerInvariant();

        return style.Contains("display:none") || style.Contains("visibility:hidden") || style.Contains("mso-hide:all");
    }

    private static void FinishLinks(IElement body)
    {
        foreach (var link in body.QuerySelectorAll("a").ToArray())
        {
            if (!IsLink(link.GetAttribute("href")))
            {
                Unwrap(link);
                continue;
            }

            link.SetAttribute("rel", "noopener noreferrer");
            link.SetAttribute("target", "_blank");
        }
    }

    private static bool IsLink(string href)
        => Uri.TryCreate(href?.Trim(), UriKind.Absolute, out var uri) && LinkSchemes.Contains(uri.Scheme);

    private static void FinishImages(IDocument document, IElement body, Func<string, HtmlImage> images)
    {
        foreach (var image in body.QuerySelectorAll("img").ToArray())
        {
            var alt = image.GetAttribute("alt");
            var contentId = ContentIdOf(image.GetAttribute("src"));

            if (contentId == null)
            {
                ReplaceWithText(document, image, alt);
                continue;
            }

            if (images == null) continue;

            var decided = images(contentId);
            if (decided?.KeepsReference == true) continue;

            if (IsImageData(decided?.Source))
            {
                image.SetAttribute("src", decided.Source);
                continue;
            }

            ReplaceWithText(document, image, string.IsNullOrWhiteSpace(alt) ? decided?.Text : alt);
        }
    }

    private static string ContentIdOf(string source)
    {
        if (source == null || !source.TrimStart().StartsWith(CidScheme, StringComparison.OrdinalIgnoreCase)) return null;

        var contentId = Uri.UnescapeDataString(source.TrimStart()[CidScheme.Length..]).Trim().Trim('<', '>');

        return contentId.Length == 0 ? null : contentId;
    }

    /// <remarks>Checked here as well as built by <see cref="HtmlImage.FromContent"/>, so a caller's resolver can supply nothing but an image.</remarks>
    private static bool IsImageData(string source)
    {
        if (string.IsNullOrEmpty(source)) return false;

        var prefix = ImageMediaTypes.Select(x => $"data:{x};base64,").FirstOrDefault(x => source.StartsWith(x, StringComparison.Ordinal));
        if (prefix == null || source.Length == prefix.Length) return false;

        return source.AsSpan(prefix.Length).IndexOfAnyExcept("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=") < 0;
    }

    private static void ReplaceWithText(IDocument document, IElement element, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) element.Remove();
        else element.Replace(document.CreateTextNode(text.Trim()));
    }

    private static void Rename(IDocument document, IElement element, string tag)
    {
        var replacement = document.CreateElement(tag);
        while (element.FirstChild != null) replacement.AppendChild(element.FirstChild);
        element.Replace(replacement);
    }

    private static void Unwrap(IElement element)
    {
        var parent = element.Parent;
        if (parent == null) return;

        while (element.FirstChild != null) parent.InsertBefore(element.FirstChild, element);
        element.Remove();
    }

    private static bool HasContent(IElement body)
        => body.TextContent.Any(x => !IsBlank(x)) || body.QuerySelector("img") != null;

    private static bool IsBlank(char character)
        => char.IsWhiteSpace(character) || character is '​' or '‌' or '‍' or '﻿';
}
