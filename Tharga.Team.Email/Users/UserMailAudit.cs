namespace Tharga.Team.Email.Users;

/// <summary>What audit entries about mail choices are recorded under, for a host reading or routing them.</summary>
public static class UserMailAudit
{
    /// <summary>The feature every entry about mail choices carries.</summary>
    public const string Feature = "usermail";

    /// <summary>The action of a choice changed in the settings, by the user or by staff.</summary>
    public const string ChoiceAction = "choice";

    /// <summary>The action of a category turned off from an unsubscribe link.</summary>
    public const string UnsubscribeAction = "unsubscribe";

    /// <summary>Metadata naming the user whose choice it is.</summary>
    public const string UserKey = "usermail.user";

    /// <summary>Metadata naming the category.</summary>
    public const string Category = "usermail.category";

    /// <summary>Metadata holding whether the user now receives the category, <c>True</c> or <c>False</c>.</summary>
    public const string Receives = "usermail.receives";
}
