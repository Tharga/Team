namespace Tharga.Team.Blazor.Framework;

/// <summary>The keys of the text automatic notices carry outside one component — the deliveries both notice views name, and the mail around the notices — listed here so a host can translate them from <see cref="ThargaTextKeys.All"/>.</summary>
public static class UserNoticeText
{
    /// <summary>The delivery that mails a notice as it happens.</summary>
    public static readonly TextKey AtOnce = new("team.userNotice.delivery.atOnce", "At once");

    /// <summary>The delivery that lists a notice in one mail a day.</summary>
    public static readonly TextKey Digest = new("team.userNotice.delivery.digest", "Daily summary");

    /// <summary>The accessible name of the choice between the deliveries.</summary>
    public static readonly TextKey Delivery = new("team.userNotice.delivery.label", "Delivery");

    /// <summary>The link under a single notice that opens the thing it is about.</summary>
    public static readonly TextKey MailOpen = new(UserNoticeTexts.OpenKey, UserNoticeTexts.Open);

    /// <summary>The subject of one mail carrying several of the same notice; <c>{0}</c> is the notice's name and <c>{1}</c> how many.</summary>
    public static readonly TextKey MailSeveralSubject = new(UserNoticeTexts.SeveralSubjectKey, UserNoticeTexts.SeveralSubject);

    /// <summary>The subject of the daily summary; <c>{0}</c> is how many notices it lists.</summary>
    public static readonly TextKey MailDigestSubject = new(UserNoticeTexts.DigestSubjectKey, UserNoticeTexts.DigestSubject);

    /// <summary>The sentence opening the daily summary.</summary>
    public static readonly TextKey MailDigestIntro = new(UserNoticeTexts.DigestIntroKey, UserNoticeTexts.DigestIntro);

    /// <summary>Every key here.</summary>
    public static readonly TextKey[] All = [AtOnce, Digest, Delivery, MailOpen, MailSeveralSubject, MailDigestSubject, MailDigestIntro];

    /// <summary>The key naming <paramref name="delivery"/>.</summary>
    public static TextKey For(UserNoticeDelivery delivery) => delivery == UserNoticeDelivery.Digest ? Digest : AtOnce;
}
