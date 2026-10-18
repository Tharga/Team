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

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Mailed, MailedTo, Site, Redirected, Withheld];
}
