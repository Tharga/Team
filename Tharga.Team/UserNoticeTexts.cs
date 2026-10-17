namespace Tharga.Team;

/// <summary>The keys and English defaults of the text notice mail carries around the notices themselves.</summary>
/// <remarks>Resolved through <see cref="ITextResolver"/>, and listed among the toolkit's text keys in <c>Tharga.Team.Blazor</c>.</remarks>
public static class UserNoticeTexts
{
    /// <summary>The key of <see cref="Open"/>.</summary>
    public const string OpenKey = "team.userNotice.mail.open";

    /// <summary>The link under a single notice that opens the thing it is about.</summary>
    public const string Open = "Open";

    /// <summary>The key of <see cref="SeveralSubject"/>.</summary>
    public const string SeveralSubjectKey = "team.userNotice.mail.severalSubject";

    /// <summary>The subject of one mail carrying several of the same notice; <c>{0}</c> is the notice's name and <c>{1}</c> how many.</summary>
    public const string SeveralSubject = "{0} ({1})";

    /// <summary>The key of <see cref="DigestSubject"/>.</summary>
    public const string DigestSubjectKey = "team.userNotice.mail.digestSubject";

    /// <summary>The subject of the daily summary; <c>{0}</c> is how many notices it lists.</summary>
    public const string DigestSubject = "Your daily summary ({0})";

    /// <summary>The key of <see cref="DigestIntro"/>.</summary>
    public const string DigestIntroKey = "team.userNotice.mail.digestIntro";

    /// <summary>The sentence opening the daily summary.</summary>
    public const string DigestIntro = "This is what happened since your last summary.";
}
