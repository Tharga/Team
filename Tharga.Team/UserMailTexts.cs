namespace Tharga.Team;

/// <summary>The keys and English defaults of the text user mail carries outside a component — the unsubscribe footer, the unsubscribe page and the invitation — and of the lines the page shares with the preferences view: always sent, and how often.</summary>
/// <remarks>Resolved through <see cref="ITextResolver"/>, which <c>Tharga.Team.Blazor</c> answers from the host's text provider, and listed among the toolkit's text keys there.</remarks>
public static class UserMailTexts
{
    /// <summary>The key of <see cref="FooterReason"/>.</summary>
    public const string FooterReasonKey = "team.userMail.footer.reason";

    /// <summary>The footer sentence saying why the mail came; <c>{0}</c> is the category's name.</summary>
    public const string FooterReason = "You are receiving this because {0} is turned on in your mail settings.";

    /// <summary>The key of <see cref="FooterUnsubscribe"/>.</summary>
    public const string FooterUnsubscribeKey = "team.userMail.footer.unsubscribe";

    /// <summary>The footer link that stops the category.</summary>
    public const string FooterUnsubscribe = "Unsubscribe";

    /// <summary>The key of <see cref="PageTitle"/>.</summary>
    public const string PageTitleKey = "team.userMail.unsubscribe.title";

    /// <summary>The unsubscribe page's title in the browser.</summary>
    public const string PageTitle = "Mail settings";

    /// <summary>The key of <see cref="PageQuestion"/>.</summary>
    public const string PageQuestionKey = "team.userMail.unsubscribe.question";

    /// <summary>What the unsubscribe page asks before anything changes; <c>{0}</c> is the category's name.</summary>
    public const string PageQuestion = "Stop receiving {0}?";

    /// <summary>The key of <see cref="PageConfirm"/>.</summary>
    public const string PageConfirmKey = "team.userMail.unsubscribe.confirm";

    /// <summary>The button that unsubscribes.</summary>
    public const string PageConfirm = "Unsubscribe";

    /// <summary>The key of <see cref="PageDone"/>.</summary>
    public const string PageDoneKey = "team.userMail.unsubscribe.done";

    /// <summary>What the unsubscribe page says once the category is off; <c>{0}</c> is the category's name.</summary>
    public const string PageDone = "You will no longer receive {0}.";

    /// <summary>The key of <see cref="PageDoneDetail"/>.</summary>
    public const string PageDoneDetailKey = "team.userMail.unsubscribe.doneDetail";

    /// <summary>Where the choice can be undone.</summary>
    public const string PageDoneDetail = "You can turn it back on at any time in your profile.";

    /// <summary>The key of <see cref="PageInvalid"/>.</summary>
    public const string PageInvalidKey = "team.userMail.unsubscribe.invalid";

    /// <summary>What the unsubscribe page says for a link it cannot read.</summary>
    public const string PageInvalid = "This link is not valid.";

    /// <summary>The key of <see cref="PageInvalidDetail"/>.</summary>
    public const string PageInvalidDetailKey = "team.userMail.unsubscribe.invalidDetail";

    /// <summary>What to do instead when the link cannot be read.</summary>
    public const string PageInvalidDetail = "It may be incomplete or out of date. You can choose which mail you receive in your profile.";

    /// <summary>The key of <see cref="InvitationSubject"/>.</summary>
    public const string InvitationSubjectKey = "team.userMail.invitation.subject";

    /// <summary>The subject of an invitation sent as mail to users; <c>{0}</c> is the team's name.</summary>
    public const string InvitationSubject = "You've been invited to join {0}";

    /// <summary>The key of <see cref="InvitationBody"/>.</summary>
    public const string InvitationBodyKey = "team.userMail.invitation.body";

    /// <summary>The text of an invitation sent as mail to users; <c>{0}</c> is the invited person's name, <c>{1}</c> the team's name and <c>{2}</c> the link that accepts it.</summary>
    public const string InvitationBody = "Hi {0},\n\nYou have been invited to join the team \"{1}\".\n\nClick the link below to accept the invitation:\n{2}\n\nIf you did not expect this invitation, you can safely ignore this email.";

    /// <summary>The key of <see cref="AlwaysSent"/>.</summary>
    public const string AlwaysSentKey = "team.userMail.alwaysSent";

    /// <summary>Beside a required category, wherever it is shown to a user, saying why it cannot be turned off; a link to the terms of use when the host has them.</summary>
    public const string AlwaysSent = "Always sent: part of the terms of use";

    /// <summary>The key of <see cref="FrequencyNone"/>.</summary>
    public const string FrequencyNoneKey = "team.userMail.frequency.none";

    /// <summary>How often a category is sent, when none was sent in the past year.</summary>
    public const string FrequencyNone = "None in the past year";

    /// <summary>The key of <see cref="FrequencyNoneRecently"/>.</summary>
    public const string FrequencyNoneRecentlyKey = "team.userMail.frequency.noneRecently";

    /// <summary>How often a category is sent, when none was sent in the shorter period the outbox keeps.</summary>
    public const string FrequencyNoneRecently = "None sent recently";

    /// <summary>The key of <see cref="FrequencyUnderOneAYear"/>.</summary>
    public const string FrequencyUnderOneAYearKey = "team.userMail.frequency.underOneAYear";

    /// <summary>How often a category is sent, when it is less than once a year.</summary>
    public const string FrequencyUnderOneAYear = "Fewer than one a year";

    /// <summary>The key of <see cref="FrequencyPerYear"/>.</summary>
    public const string FrequencyPerYearKey = "team.userMail.frequency.perYear";

    /// <summary>How often a category is sent, a few times a year; <c>{0}</c> is how many.</summary>
    public const string FrequencyPerYear = "About {0} a year";

    /// <summary>The key of <see cref="FrequencyPerMonth"/>.</summary>
    public const string FrequencyPerMonthKey = "team.userMail.frequency.perMonth";

    /// <summary>How often a category is sent, a few times a month; <c>{0}</c> is how many.</summary>
    public const string FrequencyPerMonth = "About {0} a month";

    /// <summary>The key of <see cref="FrequencyPerWeek"/>.</summary>
    public const string FrequencyPerWeekKey = "team.userMail.frequency.perWeek";

    /// <summary>How often a category is sent, about weekly or more; <c>{0}</c> is how many.</summary>
    public const string FrequencyPerWeek = "About {0} a week";
}
