namespace Tharga.Team.Blazor.Framework;

/// <summary>Localizable strings rendered by <c>SupportCaseById</c>, the conversation of one case shown by its id.</summary>
public static class SupportCaseByIdText
{
    /// <summary>Shown when there is no case with the id.</summary>
    public static readonly TextKey NotFound = new("team.support.caseById.notFound", "There is no such support case.");

    /// <summary>Shown when the reader may not read the case.</summary>
    public static readonly TextKey NotPermitted = new("team.support.caseById.notPermitted", "You may not read this support case.");

    /// <summary>Every key here, for the component building its <see cref="TextSet"/>.</summary>
    public static readonly TextKey[] All = [NotFound, NotPermitted];
}
