namespace Tharga.Team.Email.Users;

/// <summary>What audit entries about automatic notices are recorded under, for a host reading or routing them.</summary>
public static class UserNoticeAudit
{
    /// <summary>The feature every entry about notices carries.</summary>
    public const string Feature = "notices";

    /// <summary>The action of a user's choice changed, by the user or by staff.</summary>
    public const string ChoiceAction = "choice";

    /// <summary>The action of a team's default changed.</summary>
    public const string DefaultAction = "default";

    /// <summary>Metadata naming the user whose choice it is.</summary>
    public const string UserKey = "notices.user";

    /// <summary>Metadata naming the notice.</summary>
    public const string Notice = "notices.notice";

    /// <summary>Metadata holding whether the notice is now received, <c>True</c> or <c>False</c>.</summary>
    public const string Enabled = "notices.enabled";

    /// <summary>Metadata holding how the notice is now delivered, <c>AtOnce</c> or <c>Digest</c>.</summary>
    public const string Delivery = "notices.delivery";
}
