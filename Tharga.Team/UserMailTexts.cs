namespace Tharga.Team;

/// <summary>The keys and English defaults of the text user mail carries outside a component: the unsubscribe footer and the unsubscribe page.</summary>
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
}
