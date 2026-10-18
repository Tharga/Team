namespace Tharga.Team;

/// <summary>The keys and English defaults of the text contact mail carries outside a component: its footer and its unsubscribe page.</summary>
/// <remarks>
/// Resolved through <see cref="ITextResolver"/>, which <c>Tharga.Team.Blazor</c> answers from the host's text provider, and
/// listed among the toolkit's text keys there. The page also uses the user mail page's question, its line saying only that
/// category is turned off, its first button and its done heading, and the footer the user mail footer's link text, from
/// <see cref="UserMailTexts"/>.
/// </remarks>
public static class ContactMailTexts
{
    /// <summary>The key of <see cref="FooterSender"/>.</summary>
    public const string FooterSenderKey = "team.contactMail.footer.sender";

    /// <summary>The footer line saying who sends the mail; <c>{0}</c> is the host's sender line, such as its name and postal address, or else the product's name.</summary>
    public const string FooterSender = "Sent by {0}.";

    /// <summary>The key of <see cref="FooterReason"/>.</summary>
    public const string FooterReasonKey = "team.contactMail.footer.reason";

    /// <summary>The footer sentence saying why the mail came; <c>{0}</c> is the product's name.</summary>
    public const string FooterReason = "You receive this because {0} thinks it may be of interest to you.";

    /// <summary>The key of <see cref="PageTitle"/>.</summary>
    public const string PageTitleKey = "team.contactMail.unsubscribe.title";

    /// <summary>The unsubscribe page's title in the browser, for a contact.</summary>
    public const string PageTitle = "Unsubscribe";

    /// <summary>The key of <see cref="PageStopAll"/>.</summary>
    public const string PageStopAllKey = "team.contactMail.unsubscribe.stopAll";

    /// <summary>The second button, which stops every mail from the product to the address; <c>{0}</c> is the product's name.</summary>
    public const string PageStopAll = "Stop all mail from {0}";

    /// <summary>The key of <see cref="PageDoneAll"/>.</summary>
    public const string PageDoneAllKey = "team.contactMail.unsubscribe.doneAll";

    /// <summary>What the page says once every mail from the product is stopped; <c>{0}</c> is the product's name.</summary>
    public const string PageDoneAll = "You will no longer receive mail from {0}.";

    /// <summary>The key of <see cref="PageKept"/>.</summary>
    public const string PageKeptKey = "team.contactMail.unsubscribe.kept";

    /// <summary>Under what the page says once done: why the address is still kept.</summary>
    public const string PageKept = "Your address is kept only to make sure this is respected.";
}
