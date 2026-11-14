namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>SupportAnswerRouteLine</c> — where support's answer to a case goes, shown beside the answer box.</summary>
public static class SupportAnswerRouteText
{
    /// <summary>The answer is mailed; <c>{0}</c> is the address it goes to, <c>{1}</c> the support address it is sent from.</summary>
    public static readonly TextKey Mailed = new("team.support.answerRoute.mailed", "The answer is mailed to {0} from {1}.");

    /// <summary>The answer is mailed and the sending address is not known; <c>{0}</c> is the address it goes to.</summary>
    public static readonly TextKey MailedTo = new("team.support.answerRoute.mailedTo", "The answer is mailed to {0}.");

    /// <summary>The case has no mail thread, so the answer is read on the site.</summary>
    public static readonly TextKey Site = new("team.support.answerRoute.site", "The answer is shown on the site, where the case was raised; it is not mailed.");

    /// <summary>Outside production the answer goes to the override address; <c>{0}</c> is that address.</summary>
    public static readonly TextKey Redirected = new("team.support.answerRoute.redirected", "In this environment the answer is redirected to {0}.");

    /// <summary>Outside production the answer is not mailed at all.</summary>
    public static readonly TextKey Withheld = new("team.support.answerRoute.withheld", "In this environment the answer is not sent.");

    /// <summary>The label of the choice of address the answer is sent from.</summary>
    public static readonly TextKey SendFrom = new("team.support.answerRoute.sendFrom", "Send from");

    /// <summary>The label of the choice to sign the answer with the answerer's own name.</summary>
    public static readonly TextKey SignWithName = new("team.support.answerRoute.signWithName", "Sign with my name");

    /// <summary>Offered on a case with no mail thread whose author support can write to; <c>{0}</c> is the address the host resolved.</summary>
    public static readonly TextKey AnswerByMail = new("team.support.answerRoute.answerByMail", "Answer by mail to {0}");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Mailed, MailedTo, Site, Redirected, Withheld, SendFrom, SignWithName, AnswerByMail];
}
