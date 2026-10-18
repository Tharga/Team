namespace Tharga.Team.Blazor.Framework;

/// <summary>The keys of the text contact mail carries outside components — its footer and its unsubscribe page — listed here so a host can translate them from <see cref="ThargaTextKeys.All"/>.</summary>
/// <remarks>The page also uses keys of <see cref="UserMailText"/>: the question, the line saying only that category is turned off, the first button, the done heading and the footer's link text.</remarks>
public static class ContactMailText
{
    /// <summary>The footer line saying who sends the mail; <c>{0}</c> is the host's sender line, or else the product's name.</summary>
    public static readonly TextKey FooterSender = new(ContactMailTexts.FooterSenderKey, ContactMailTexts.FooterSender);

    /// <summary>The footer sentence saying why the mail came; <c>{0}</c> is the product's name.</summary>
    public static readonly TextKey FooterReason = new(ContactMailTexts.FooterReasonKey, ContactMailTexts.FooterReason);

    /// <summary>The unsubscribe page's title in the browser, for a contact.</summary>
    public static readonly TextKey PageTitle = new(ContactMailTexts.PageTitleKey, ContactMailTexts.PageTitle);

    /// <summary>The second button, which stops every mail from the product to the address; <c>{0}</c> is the product's name.</summary>
    public static readonly TextKey PageStopAll = new(ContactMailTexts.PageStopAllKey, ContactMailTexts.PageStopAll);

    /// <summary>What the page says once every mail from the product is stopped; <c>{0}</c> is the product's name.</summary>
    public static readonly TextKey PageDoneAll = new(ContactMailTexts.PageDoneAllKey, ContactMailTexts.PageDoneAll);

    /// <summary>Under what the page says once done: why the address is still kept.</summary>
    public static readonly TextKey PageKept = new(ContactMailTexts.PageKeptKey, ContactMailTexts.PageKept);

    /// <summary>Every key here.</summary>
    public static readonly TextKey[] All = [FooterSender, FooterReason, PageTitle, PageStopAll, PageDoneAll, PageKept];
}
