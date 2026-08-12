using System.Text.RegularExpressions;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// Every team read in <c>TeamRepository</c> excludes soft-deleted teams, unless its name says otherwise.
/// </summary>
/// <remarks>
/// <b>This is what makes soft-delete-on-by-default honest.</b> The filter lives at the store so no caller
/// has to remember it, and a read added later without it is a silent data leak rather than a visible bug —
/// a deleted team that still lists, still resolves, and (through the membership read) still authorizes.
/// Spot-checking the reads that existed on the day is not a guard; this is.
/// <para>
/// A source scan, because the rule is a property of each query expression and nothing compiled records it.
/// Same reasoning as <c>DialogButtonOrderTests</c> and <c>ConsentBadgeOrderTests</c> — with the lesson
/// those two taught applied: the marker is <c>DeletedAt</c>, a field name this codebase is not otherwise
/// changing, rather than a display string that a later feature would silently invalidate.
/// </para>
/// <para>
/// <b>Opting out is by name, not by attribute.</b> A method called <c>…IncludingDeleted…</c> is exempt,
/// so the exemption is visible at every call site rather than only at the declaration. That is the same
/// reason the unfiltered reads are separate methods instead of a defaulted boolean.
/// </para>
/// </remarks>
public class SoftDeleteReadFilterGuardTests
{
    private const string DeletedMarker = "DeletedAt";
    private const string ExemptNameFragment = "IncludingDeleted";
    private const string RepositoryPath = "Tharga.Team.MongoDB/TeamRepository.cs";

    /// <summary>A public method returning teams — the shape that can leak one.</summary>
    private static readonly Regex TeamRead = new(
        @"public\s+(?:async\s+)?(?:Task<TTeamEntity>|IAsyncEnumerable<TTeamEntity>)\s+(\w+)\s*\([^)]*\)\s*\{(.*?)\n    \}",
        RegexOptions.Singleline | RegexOptions.Compiled);

    private static string RepositorySource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.MongoDB"))) dir = dir.Parent;
        Assert.NotNull(dir);

        var full = Path.Combine(dir.FullName, RepositoryPath.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(full), $"'{RepositoryPath}' was not found — the guard is scanning nothing.");

        return File.ReadAllText(full);
    }

    private static (string Name, string Body)[] TeamReads(string source)
        => [.. TeamRead.Matches(source).Select(m => (m.Groups[1].Value, m.Groups[2].Value))];

    /// <summary>The guard.</summary>
    [Fact]
    public void EveryTeamRead_ExcludesSoftDeletedTeams()
    {
        var offenders = TeamReads(RepositorySource())
            .Where(x => !x.Name.Contains(ExemptNameFragment, StringComparison.Ordinal))
            .Where(x => !x.Body.Contains(DeletedMarker, StringComparison.Ordinal))
            .Select(x => x.Name)
            .ToArray();

        Assert.True(offenders.Length == 0,
            "These team reads do not exclude soft-deleted teams, so a deleted team leaks back into the " +
            "application — listing, resolving, and through the membership read still authorizing: " +
            string.Join(", ", offenders) +
            $". Filter on '{DeletedMarker} == null', or name the method '…{ExemptNameFragment}…' if seeing " +
            "deleted teams is the point.");
    }

    /// <summary>
    /// The self-check that matters most: a scan matching nothing passes forever while reading as "every
    /// read is checked". Both counts are asserted — that reads were found at all, and that at least one
    /// exempt read exists, so the exemption path is exercised rather than assumed.
    /// </summary>
    [Fact]
    public void TheGuard_ActuallyFindsTheReads()
    {
        var reads = TeamReads(RepositorySource());

        Assert.True(reads.Length >= 4,
            $"Only {reads.Length} team read(s) found in {RepositoryPath}; the regex has stopped matching " +
            "the code and the guard above is checking almost nothing.");

        Assert.Contains(reads, x => x.Name.Contains(ExemptNameFragment, StringComparison.Ordinal));
        Assert.Contains(reads, x => !x.Name.Contains(ExemptNameFragment, StringComparison.Ordinal));
    }

    /// <summary>And that it rejects the shape it exists to catch.</summary>
    [Fact]
    public void TheGuard_DetectsAnUnfilteredRead()
    {
        const string unfiltered = """
            public IAsyncEnumerable<TTeamEntity> GetTeamsBySomethingAsync(string value)
                {
                    return _collection.GetAsync(x => x.Something == value);
                }
            """;

        var reads = TeamReads(unfiltered);

        Assert.Single(reads);
        Assert.DoesNotContain(DeletedMarker, reads[0].Body, StringComparison.Ordinal);
        Assert.DoesNotContain(ExemptNameFragment, reads[0].Name, StringComparison.Ordinal);
    }

    /// <summary>
    /// <b>The wider lesson, recorded as a test comment because no scan can enforce it.</b> Soft delete's
    /// worst defect so far was not an unfiltered read — it was <c>GetRandomUnsusedTeamKey</c>, a *write*
    /// path that asked a read whether a key was free and got "yes" for a soft-deleted team. A guard over
    /// reads would never have caught it.
    /// <para>
    /// So: when adding anything that consults a team read to make a decision, ask whether a soft-deleted
    /// team gives the wrong answer. The reads are guarded above; the questions asked of them are not.
    /// </para>
    /// </summary>
    [Fact]
    public void TheKeyReservationPathIsCoveredByItsOwnTest()
    {
        // Left as an executable pointer: SoftDeletedKeyReservationTests covers the write-that-reads case,
        // which this source scan is structurally unable to see.
        Assert.True(true);
    }
}
