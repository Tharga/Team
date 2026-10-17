namespace Tharga.Team.Blazor.Framework;

/// <summary>The keys of the text user mail carries outside components — the unsubscribe footer and page, the built-in categories and the invitation — listed here so a host can translate them from <see cref="ThargaTextKeys.All"/>.</summary>
public static class UserMailText
{
    /// <summary>The footer sentence saying why the mail came; <c>{0}</c> is the category's name.</summary>
    public static readonly TextKey FooterReason = new(UserMailTexts.FooterReasonKey, UserMailTexts.FooterReason);

    /// <summary>The footer link that stops the category.</summary>
    public static readonly TextKey FooterUnsubscribe = new(UserMailTexts.FooterUnsubscribeKey, UserMailTexts.FooterUnsubscribe);

    /// <summary>The unsubscribe page's title in the browser.</summary>
    public static readonly TextKey PageTitle = new(UserMailTexts.PageTitleKey, UserMailTexts.PageTitle);

    /// <summary>What the unsubscribe page asks; <c>{0}</c> is the category's name.</summary>
    public static readonly TextKey PageQuestion = new(UserMailTexts.PageQuestionKey, UserMailTexts.PageQuestion);

    /// <summary>The unsubscribe page's button.</summary>
    public static readonly TextKey PageConfirm = new(UserMailTexts.PageConfirmKey, UserMailTexts.PageConfirm);

    /// <summary>What the unsubscribe page says once done; <c>{0}</c> is the category's name.</summary>
    public static readonly TextKey PageDone = new(UserMailTexts.PageDoneKey, UserMailTexts.PageDone);

    /// <summary>Where the choice can be undone.</summary>
    public static readonly TextKey PageDoneDetail = new(UserMailTexts.PageDoneDetailKey, UserMailTexts.PageDoneDetail);

    /// <summary>What the unsubscribe page says for a link it cannot read.</summary>
    public static readonly TextKey PageInvalid = new(UserMailTexts.PageInvalidKey, UserMailTexts.PageInvalid);

    /// <summary>What to do instead when the link cannot be read.</summary>
    public static readonly TextKey PageInvalidDetail = new(UserMailTexts.PageInvalidDetailKey, UserMailTexts.PageInvalidDetail);

    /// <summary>The name of the built-in account and security category.</summary>
    public static readonly TextKey AccountName = new(UserMailCategories.Account.NameKey, UserMailCategories.Account.Name);

    /// <summary>The description of the built-in account and security category.</summary>
    public static readonly TextKey AccountDescription = new(UserMailCategories.Account.DescriptionKey, UserMailCategories.Account.Description);

    /// <summary>The name of the built-in service messages category.</summary>
    public static readonly TextKey ServiceName = new(UserMailCategories.Service.NameKey, UserMailCategories.Service.Name);

    /// <summary>The description of the built-in service messages category.</summary>
    public static readonly TextKey ServiceDescription = new(UserMailCategories.Service.DescriptionKey, UserMailCategories.Service.Description);

    /// <summary>The subject of an invitation sent as mail to users; <c>{0}</c> is the team's name.</summary>
    public static readonly TextKey InvitationSubject = new(UserMailTexts.InvitationSubjectKey, UserMailTexts.InvitationSubject);

    /// <summary>The text of an invitation sent as mail to users; <c>{0}</c> is the name, <c>{1}</c> the team, <c>{2}</c> the link.</summary>
    public static readonly TextKey InvitationBody = new(UserMailTexts.InvitationBodyKey, UserMailTexts.InvitationBody);

    /// <summary>Every key here.</summary>
    public static readonly TextKey[] All = [FooterReason, FooterUnsubscribe, PageTitle, PageQuestion, PageConfirm, PageDone, PageDoneDetail, PageInvalid, PageInvalidDetail, AccountName, AccountDescription, ServiceName, ServiceDescription, InvitationSubject, InvitationBody];
}
