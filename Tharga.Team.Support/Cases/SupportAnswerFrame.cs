using System.Globalization;
using System.Net;
using Tharga.Team.Email.Users;

namespace Tharga.Team.Support.Cases;

/// <summary>What an answer mail says for support around the answer itself: the signature after it and the line above each earlier entry it quotes, resolved once per mail.</summary>
/// <remarks>
/// <b>No staff member is named.</b> An entry written by support or by the assistant is attributed to the support name; a
/// customer's own entry keeps the name it was written under.
/// </remarks>
internal sealed class SupportAnswerFrame
{
    private const string DateFormat = "yyyy-MM-dd HH:mm";

    private const string NamePlaceholder = "{Name}";
    private const string NewlinePlaceholder = "{newline}";

    private readonly string _supportName;
    private readonly string _attribution;
    private readonly string _closing;
    private readonly TimeZoneInfo _zone;

    private SupportAnswerFrame(IReadOnlyList<string> signature, string supportName, string attribution, TimeZoneInfo zone, string closing, bool nameTheAnswerer, SupportQuoteStyle quoteStyle)
    {
        QuoteStyle = quoteStyle;
        Signature = signature;
        _supportName = supportName;
        _attribution = attribution;
        _zone = zone;
        _closing = closing;
        NameTheAnswerer = nameTheAnswerer;
    }

    /// <summary>No signature, entries attributed in English, dates in UTC, answers signed with no name.</summary>
    public static SupportAnswerFrame Default { get; } = new([], SupportMailTexts.SupportName, SupportMailTexts.QuoteAttribution, TimeZoneInfo.Utc, SupportMailTexts.Closing, false, SupportQuoteStyle.Nested);

    /// <summary>How the conversation so far is quoted.</summary>
    public SupportQuoteStyle QuoteStyle { get; }

    /// <summary>Whether an answer is signed with the name of the one who wrote it unless they choose otherwise.</summary>
    public bool NameTheAnswerer { get; }

    /// <summary>The frame for the host's options, its text through the host's text provider in the current culture.</summary>
    public static async Task<SupportAnswerFrame> CreateAsync(SupportAnswerOptions options, ITextResolver texts)
    {
        options ??= new SupportAnswerOptions();
        var text = new UserMailText(texts);

        var name = string.IsNullOrWhiteSpace(options.SupportName)
            ? await text.GetAsync(SupportMailTexts.SupportNameKey, SupportMailTexts.SupportName)
            : options.SupportName.Trim();

        var closing = string.IsNullOrWhiteSpace(options.Closing)
            ? await text.GetAsync(SupportMailTexts.ClosingKey, SupportMailTexts.Closing)
            : options.Closing;

        return new SupportAnswerFrame(Lines(options.Signature), name, await text.GetAsync(SupportMailTexts.QuoteAttributionKey, SupportMailTexts.QuoteAttribution), Zone(options.TimeZoneId), closing, options.NameTheAnswerer, options.QuoteStyle);
    }

    /// <summary>The signature's lines, without blank lines before or after; empty for none.</summary>
    public IReadOnlyList<string> Signature { get; }

    /// <summary>The signature as text, or null for none.</summary>
    public string SignatureText => SignatureTextFor(null);

    /// <summary>The signature as one encoded paragraph of class <c>signature</c>, or null for none.</summary>
    public string SignatureHtml => SignatureHtmlFor(null);

    /// <summary>The lines signed after an answer: the closing with the answerer's name when the answer was signed with it, then the host's signature.</summary>
    public IReadOnlyList<string> SignatureOf(SupportMessage answer)
    {
        if (answer is not { SignedWithName: true } || string.IsNullOrWhiteSpace(answer.AuthorName)) return Signature;

        return [.. Lines([ClosingOf(answer.AuthorName)]), .. Signature];
    }

    /// <summary>The signature of an answer as text, or null for none.</summary>
    public string SignatureTextFor(SupportMessage answer) => SignatureOf(answer) is { Count: > 0 } lines ? string.Join("\n", lines) : null;

    /// <summary>The signature of an answer as one encoded paragraph of class <c>signature</c>, or null for none.</summary>
    public string SignatureHtmlFor(SupportMessage answer)
        => SignatureOf(answer) is { Count: > 0 } lines ? $"<p class=\"signature\">{string.Join("<br>", lines.Select(WebUtility.HtmlEncode))}</p>" : null;

    private string ClosingOf(string name) => _closing.Replace(NamePlaceholder, name).Replace(NewlinePlaceholder, "\n");

    /// <summary>The line above a quoted entry: when it was written, in the host's time zone, and by whom — support's name for an entry written by support, unless it was signed with its writer's name.</summary>
    public string Attribution(SupportMessage quoted)
    {
        var who = IsSupport(quoted) && !(quoted.SignedWithName && !string.IsNullOrWhiteSpace(quoted.AuthorName)) ? _supportName : quoted.AuthorName;
        var when = When(quoted.SentAt);

        try
        {
            return string.Format(CultureInfo.CurrentCulture, _attribution, when, who);
        }
        catch (FormatException)
        {
            return string.Format(CultureInfo.CurrentCulture, SupportMailTexts.QuoteAttribution, when, who);
        }
    }

    /// <summary>The time as <c>yyyy-MM-dd HH:mm</c> in the host's zone, followed by <c>UTC</c> when that is UTC.</summary>
    public string When(DateTime sentAt)
    {
        var utc = sentAt.Kind == DateTimeKind.Local ? sentAt.ToUniversalTime() : DateTime.SpecifyKind(sentAt, DateTimeKind.Utc);
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, _zone).ToString(DateFormat, CultureInfo.InvariantCulture);

        return IsUtc(_zone) ? $"{local} UTC" : local;
    }

    /// <summary>Whether support wrote the entry: a person answering from the support side, or the assistant.</summary>
    public static bool IsSupport(SupportMessage message) => message.AsSupport || message.Kind == SupportMessageKind.Assistant;

    /// <summary>The time zone with this id, UTC when none is given.</summary>
    /// <exception cref="InvalidOperationException">The id is not a time zone this system knows.</exception>
    public static TimeZoneInfo Zone(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return TimeZoneInfo.Utc;

        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id.Trim());
        }
        catch (Exception e) when (e is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new InvalidOperationException($"{nameof(SupportAnswerOptions)}.{nameof(SupportAnswerOptions.TimeZoneId)} '{id}' is not a time zone this system knows.", e);
        }
    }

    private static bool IsUtc(TimeZoneInfo zone) => zone.BaseUtcOffset == TimeSpan.Zero && !zone.SupportsDaylightSavingTime;

    private static string[] Lines(string[] signature)
    {
        var lines = (signature ?? []).SelectMany(x => (x ?? string.Empty).Replace("\r\n", "\n").Split('\n')).Select(x => x.TrimEnd()).ToList();

        while (lines.Count > 0 && lines[0].Length == 0) lines.RemoveAt(0);
        while (lines.Count > 0 && lines[^1].Length == 0) lines.RemoveAt(lines.Count - 1);

        return [.. lines];
    }
}
