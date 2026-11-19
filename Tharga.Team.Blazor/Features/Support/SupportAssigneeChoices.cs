using Tharga.Team.Support.Cases;

namespace Tharga.Team.Blazor.Features.Support;

/// <summary>Who a support list offers as an assignee: the people the service says may be chosen, and whoever the listed cases already have.</summary>
internal static class SupportAssigneeChoices
{
    /// <summary>
    /// The people a case may be assigned to, and whether the service could say. <c>false</c> for a reader without a staff grant and for a host's
    /// own case service that does not support assigning, so a control is left out rather than shown failing.
    /// </summary>
    public static async Task<(SupportCaseAssignee[] Staff, bool Available)> StaffAsync(ISupportCaseService cases)
    {
        try
        {
            return (await cases.GetAssignableStaffAsync() ?? [], true);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException or NotSupportedException)
        {
            return ([], false);
        }
    }

    /// <summary>The staff and the assignees of the cases listed, once each, by name: a person who left is still found by the cases they hold.</summary>
    public static SupportCaseAssignee[] Of(IEnumerable<SupportCaseAssignee> staff, IEnumerable<SupportCase> cases)
        => [.. staff
            .Concat(cases.Select(x => x.Assignee).Where(x => x != null))
            .GroupBy(x => x.Key)
            .Select(x => x.First())
            .OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase)];
}
