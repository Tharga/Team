namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>SupportDeskStatus</c> — how a support person is shown to members.</summary>
public static class SupportDeskStatusText
{
    /// <summary>The label of the choice.</summary>
    public static readonly TextKey Label = new("team.support.desk.status.label", "Shown to members as");

    /// <summary>Online while active, away when idle.</summary>
    public static readonly TextKey Automatic = new("team.support.desk.status.automatic", "Automatic");

    /// <summary>There to answer.</summary>
    public static readonly TextKey Online = new("team.support.desk.status.online", "Online");

    /// <summary>Not answering now.</summary>
    public static readonly TextKey Away = new("team.support.desk.status.away", "Away");

    /// <summary>Answering without being shown.</summary>
    public static readonly TextKey Hidden = new("team.support.desk.status.hidden", "Hidden");

    /// <summary>What choosing Automatic means.</summary>
    public static readonly TextKey AutomaticHint = new("team.support.desk.status.automaticHint",
        "Members see that support is online while you are active here. Ten minutes without doing anything counts as away.");

    /// <summary>What choosing Online means.</summary>
    public static readonly TextKey OnlineHint = new("team.support.desk.status.onlineHint",
        "Members see that support is online for as long as you leave this on, whether you are active or not. It lapses to away after a long time without activity.");

    /// <summary>What choosing Away means.</summary>
    public static readonly TextKey AwayHint = new("team.support.desk.status.awayHint", "Members are told support will reply later.");

    /// <summary>What choosing Hidden means.</summary>
    public static readonly TextKey HiddenHint = new("team.support.desk.status.hiddenHint", "You can answer as usual, and members are told nothing about whether you are there.");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [Label, Automatic, Online, Away, Hidden, AutomaticHint, OnlineHint, AwayHint, HiddenHint];
}
