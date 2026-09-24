namespace Tharga.Team.Sample.Framework.Team;

/// <summary>
/// Demo state for the member-row extension points: a per-member "project", held in memory.
/// </summary>
/// <remarks>
/// <b>Deliberately not persisted.</b> Tharga/Team#294 asked for a place to put UI for a host's own member
/// field and explicitly set persistence aside — a host extending the member type writes its own storage
/// path. Keeping this in memory demonstrates the hooks without implying the toolkit stores the field, which
/// it does not. Restarting the sample clears it, as it should.
/// </remarks>
public sealed class MemberProjectDemo
{
    private static readonly string[] Projects = ["Apollo", "Borealis", "Cassini", "—"];

    private readonly Dictionary<string, string> _byMemberKey = new(StringComparer.Ordinal);

    public string Get(string memberKey)
        => memberKey != null && _byMemberKey.TryGetValue(memberKey, out var project) ? project : "—";

    /// <summary>Moves the member to the next project in the list, so one click produces a visible change.</summary>
    public string Cycle(string memberKey)
    {
        if (memberKey == null) return "—";

        var next = Projects[(Array.IndexOf(Projects, Get(memberKey)) + 1) % Projects.Length];
        _byMemberKey[memberKey] = next;
        return next;
    }
}
