namespace Tharga.Team.Blazor.Framework;

/// <summary>
/// Localizable strings rendered by <c>SupportAssigneePicker</c> — who on the support staff a case is assigned to.
/// </summary>
public static class SupportAssigneeText
{
    /// <summary>Names the choice of who the case is assigned to, beside the picker and for a screen reader.</summary>
    public static readonly TextKey AssignedTo = new("team.support.assignee.label", "Assigned to");

    /// <summary>Shown in the picker while nobody is assigned.</summary>
    public static readonly TextKey Nobody = new("team.support.assignee.nobody", "Nobody");

    /// <summary>Shown when the service refused the person chosen, for example one who is no longer offered.</summary>
    public static readonly TextKey Failed = new("team.support.assignee.failed", "The case could not be assigned to that person. The case has been refreshed.");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [AssignedTo, Nobody, Failed];
}
