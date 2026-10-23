using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.Extensions.Options;

namespace Tharga.Team.Email.Users;

/// <summary>Writes the mail for what waits in a user's stream: one notice as itself, several of one notice as a list, and the daily summary grouped by notice.</summary>
/// <remarks>
/// Every string goes through the host's text resolver, and a translated template that does not fill falls back to the
/// English one. Values are HTML-encoded in bodies; links are the item's site path on <see cref="UserMailOptions.BaseUrl"/>.
/// </remarks>
internal sealed class UserNoticeMail(UserNoticeRegistry notices, UserMailText text, IOptions<UserMailOptions> mailOptions)
{
    /// <summary>The stream of a user's notices of one kind delivered at once.</summary>
    public static string AtOnceStream(string noticeKey) => $"{AtOncePrefix}{noticeKey}";

    /// <summary>The stream of a user's daily summary of one mail category.</summary>
    public static string DigestStream(string categoryKey) => $"{DigestPrefix}{categoryKey}";

    /// <summary>Whether the stream is a daily summary.</summary>
    public static bool IsDigest(string stream) => stream?.StartsWith(DigestPrefix, StringComparison.Ordinal) == true;

    private const string AtOncePrefix = "notice:";
    private const string DigestPrefix = "digest:";

    /// <summary>The mail to <paramref name="userKey"/> for <paramref name="items"/>, every one of a notice in the catalogue and of one stream; about a team when all its notices are.</summary>
    public async Task<UserMail> ComposeAsync(string userKey, string stream, UserNoticeItem[] items)
        => (await ComposeContentAsync(userKey, stream, items)) with { TeamKey = TeamOf(items) };

    /// <remarks>A summary can hold notices of several teams, and then belongs to none of them.</remarks>
    private static string TeamOf(UserNoticeItem[] items)
    {
        string[] teams = [.. items.Select(x => x.TeamKey).Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).Take(2)];

        return teams.Length == 1 ? teams[0] : null;
    }

    private async Task<UserMail> ComposeContentAsync(string userKey, string stream, UserNoticeItem[] items)
    {
        var category = items[0].Category;

        if (IsDigest(stream)) return Mail(userKey, category, await text.FormatAsync(UserNoticeTexts.DigestSubjectKey, UserNoticeTexts.DigestSubject, items.Length), await DigestBodyAsync(items));

        var notice = notices.Find(items[0].NoticeKey);
        if (items.Length == 1)
        {
            var item = items[0];
            return Mail(userKey, category, await SubjectAsync(notice, item), await SingleBodyAsync(notice, item));
        }

        var name = await text.GetAsync(notice.NameKey, notice.Name);
        return Mail(userKey, category, await text.FormatAsync(UserNoticeTexts.SeveralSubjectKey, UserNoticeTexts.SeveralSubject, name, items.Length), await ListAsync(notice, items));
    }

    /// <summary>The subject line of one occurrence.</summary>
    public async Task<string> SubjectAsync(UserNoticeType notice, UserNoticeItem item)
    {
        var template = await text.GetAsync(notice.SubjectKey, notice.Subject);

        return UserMailQueue.OneLine(Fill(template, notice.Subject, item, x => x));
    }

    private async Task<string> SingleBodyAsync(UserNoticeType notice, UserNoticeItem item)
    {
        var body = new StringBuilder();

        if (!string.IsNullOrWhiteSpace(notice.Body))
        {
            var template = await text.GetAsync(notice.BodyKey, notice.Body);
            body.Append(Fill(template, notice.Body, item, WebUtility.HtmlEncode));
        }
        else if (item.LinkPath == null)
        {
            body.Append("<p>").Append(WebUtility.HtmlEncode(await SubjectAsync(notice, item))).Append("</p>");
        }

        if (item.LinkPath != null)
        {
            var open = await text.GetAsync(UserNoticeTexts.OpenKey, UserNoticeTexts.Open);
            body.Append("<p><a href=\"").Append(WebUtility.HtmlEncode(Link(item))).Append("\">").Append(WebUtility.HtmlEncode(open)).Append("</a></p>");
        }

        return body.ToString();
    }

    private async Task<string> ListAsync(UserNoticeType notice, IEnumerable<UserNoticeItem> items)
    {
        var list = new StringBuilder("<ul>");
        foreach (var item in items)
        {
            var subject = WebUtility.HtmlEncode(await SubjectAsync(notice, item));
            list.Append("<li>");
            if (item.LinkPath == null) list.Append(subject);
            else list.Append("<a href=\"").Append(WebUtility.HtmlEncode(Link(item))).Append("\">").Append(subject).Append("</a>");
            list.Append("</li>");
        }

        return list.Append("</ul>").ToString();
    }

    private async Task<string> DigestBodyAsync(UserNoticeItem[] items)
    {
        var body = new StringBuilder("<p>").Append(WebUtility.HtmlEncode(await text.GetAsync(UserNoticeTexts.DigestIntroKey, UserNoticeTexts.DigestIntro))).Append("</p>");

        foreach (var group in items.GroupBy(x => x.NoticeKey, StringComparer.Ordinal).OrderBy(x => Order(x.Key)))
        {
            var notice = notices.Find(group.Key);
            body.Append("<h3>").Append(WebUtility.HtmlEncode(await text.GetAsync(notice.NameKey, notice.Name))).Append("</h3>");
            body.Append(await ListAsync(notice, group));
        }

        return body.ToString();
    }

    private int Order(string noticeKey)
    {
        for (var i = 0; i < notices.All.Count; i++)
        {
            if (notices.All[i].Key == noticeKey) return i;
        }

        return int.MaxValue;
    }

    /// <summary>The template filled from the item, or the English one when a translation names a value the notice does not have or is malformed.</summary>
    private static string Fill(string template, string english, UserNoticeItem item, Func<string, string> encode)
    {
        var values = new Dictionary<string, string>(item.Values ?? new Dictionary<string, string>(), StringComparer.Ordinal)
        {
            [UserNoticeType.SubjectPlaceholder] = item.SubjectText ?? item.SubjectId
        };

        try
        {
            return UserNoticeTemplate.Render(template, values, encode);
        }
        catch (FormatException)
        {
            return UserNoticeTemplate.Render(english ?? string.Empty, values, encode);
        }
    }

    private string Link(UserNoticeItem item) => $"{mailOptions.Value.BaseUrl?.TrimEnd('/')}{item.LinkPath}";

    private static UserMail Mail(string userKey, string category, string subject, string bodyHtml) => new()
    {
        RecipientUserKey = userKey,
        Category = category,
        Subject = string.IsNullOrWhiteSpace(subject) ? category : subject,
        BodyHtml = bodyHtml,
        Kind = UserMailKind.Notice
    };

    /// <summary>The key a mail is queued under so a claimant taking over queues it once: per day for a summary, per mail otherwise.</summary>
    public static string MailKey(string userKey, string stream, DateOnly localDate)
    {
        var key = IsDigest(stream)
            ? $"notice:{stream}:{localDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}:{userKey}"
            : $"notice:{stream}:{Guid.NewGuid():N}:{userKey}";

        return key.Length <= 200 ? key : $"notice:{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key)))}";
    }
}
