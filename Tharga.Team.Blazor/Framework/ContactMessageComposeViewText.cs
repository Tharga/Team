namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>ContactMessageComposeView</c>, where staff write to the contacts a host lists.</summary>
public static class ContactMessageComposeViewText
{
    /// <summary>When the reader may not write to contacts.</summary>
    public static readonly TextKey NotPermitted = new("team.contactMessage.compose.notPermitted", "You may not write to contacts.");

    /// <summary>One sentence above the form.</summary>
    public static readonly TextKey Explanation = new("team.contactMessage.compose.explanation", "Write to the people listed. Each address receives it once, as a mail with a link to stop such mail.");

    /// <summary>The heading of the list of who receives the message.</summary>
    public static readonly TextKey RecipientList = new("team.contactMessage.compose.recipientList", "Recipients");

    /// <summary>When nobody is listed.</summary>
    public static readonly TextKey NoAddressees = new("team.contactMessage.compose.noAddressees", "Nobody is listed.");

    /// <summary>How many addresses the message reaches; <c>{0}</c> is the count.</summary>
    public static readonly TextKey Reaches = new("team.contactMessage.compose.reaches", "Reaches {0} addresses.");

    /// <summary>How many listed addresses opted out; <c>{0}</c> is the count.</summary>
    public static readonly TextKey OptedOutCount = new("team.contactMessage.compose.optedOutCount", "{0} have opted out and will not receive it.");

    /// <summary>How many listed addresses are left out; <c>{0}</c> is the count.</summary>
    public static readonly TextKey LeftOutCount = new("team.contactMessage.compose.leftOutCount", "{0} are left out.");

    /// <summary>Marks an address that opted out.</summary>
    public static readonly TextKey OptedOut = new("team.contactMessage.compose.optedOut", "Opted out");

    /// <summary>Why an address that opted out of the category will not receive it; <c>{0}</c> is the category's name.</summary>
    public static readonly TextKey OptedOutDetail = new("team.contactMessage.compose.optedOutDetail", "Has opted out of {0}.");

    /// <summary>Why an address that opted out of all contact mail will not receive it.</summary>
    public static readonly TextKey OptedOutOfAllDetail = new("team.contactMessage.compose.optedOutOfAllDetail", "Has opted out of all mail to contacts.");

    /// <summary>Marks a row the host left out; <c>{0}</c> is the host's reason.</summary>
    public static readonly TextKey LeftOut = new("team.contactMessage.compose.leftOut", "Left out: {0}");

    /// <summary>Marks a row whose address is not a mail address.</summary>
    public static readonly TextKey InvalidAddress = new("team.contactMessage.compose.invalidAddress", "Left out: not a mail address");

    /// <summary>The button that shows more of the list.</summary>
    public static readonly TextKey MoreRecipients = new("team.contactMessage.compose.moreRecipients", "Show more");

    /// <summary>When the list cannot be checked; <c>{0}</c> is why.</summary>
    public static readonly TextKey ListFailed = new("team.contactMessage.compose.listFailed", "The list cannot be checked. {0}");

    /// <summary>Names the category picker.</summary>
    public static readonly TextKey Category = new("team.contactMessage.compose.category", "Category");

    /// <summary>Names the subject field.</summary>
    public static readonly TextKey Subject = new("team.contactMessage.compose.subject", "Subject");

    /// <summary>Names the message editor.</summary>
    public static readonly TextKey Body = new("team.contactMessage.compose.body", "Message");

    /// <summary>The button that sends.</summary>
    public static readonly TextKey Send = new("team.contactMessage.compose.send", "Send");

    /// <summary>The confirmation's title.</summary>
    public static readonly TextKey ConfirmTitle = new("team.contactMessage.compose.confirmTitle", "Send to contacts");

    /// <summary>The confirmation's question; <c>{0}</c> is the subject, <c>{1}</c> how many addresses it reaches.</summary>
    public static readonly TextKey ConfirmBody = new("team.contactMessage.compose.confirmBody", "Send \"{0}\" to {1} addresses?");

    /// <summary>The confirmation's button that sends.</summary>
    public static readonly TextKey ConfirmSend = new("team.contactMessage.compose.confirmSend", "Send");

    /// <summary>The confirmation's button that does not send.</summary>
    public static readonly TextKey ConfirmCancel = new("team.contactMessage.compose.confirmCancel", "Cancel");

    /// <summary>Shown once the message is queued; <c>{0}</c> is how many addresses it reaches.</summary>
    public static readonly TextKey Sent = new("team.contactMessage.compose.sent", "The message is on its way to {0} addresses.");

    /// <summary>Shown when the message was refused; <c>{0}</c> is why.</summary>
    public static readonly TextKey SendFailed = new("team.contactMessage.compose.sendFailed", "The message was not sent. {0}");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All =
    [
        NotPermitted, Explanation, RecipientList, NoAddressees, Reaches, OptedOutCount, LeftOutCount, OptedOut, OptedOutDetail, OptedOutOfAllDetail,
        LeftOut, InvalidAddress, MoreRecipients, ListFailed, Category, Subject, Body, Send, ConfirmTitle, ConfirmBody, ConfirmSend, ConfirmCancel, Sent,
        SendFailed
    ];
}
