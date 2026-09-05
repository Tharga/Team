using System.Text.RegularExpressions;

namespace Tharga.Team.MongoDB.Tests;

/// <summary>
/// The index that lets a short invitation token find its team, and the reason it must not be unique.
/// </summary>
/// <remarks>
/// <b>Unique is the tempting mistake, and it would break creating teams.</b> A code identifying one
/// invitation reads like something a unique index should enforce — but the index is multikey over the members
/// array, most members carry no invitation, and their entries index as null. Uniqueness is enforced
/// <i>across</i> documents, so the second team containing a member without an invitation collides with the
/// first and fails to save. <c>partialFilterExpression</c> does not rescue it: it selects whole documents, so
/// a team holding both invited and ordinary members still indexes the nulls.
/// <para>
/// Uniqueness comes from the token instead — 128 bits from a cryptographic source — and
/// <c>TeamRepository.GetByInviteKeyAsync</c> refuses an ambiguous match rather than choosing between teams.
/// </para>
/// <para>
/// <b>A source scan, and deliberately so.</b> <c>TeamRepositoryCollection</c> cannot be constructed in a test
/// — its base does real work against a Mongo service in its constructor, which is why
/// <c>RegisterUserRepositoryTests</c> inspects registrations rather than building the provider. There is also
/// no MongoDB server in this suite, so <b>this test cannot prove the behaviour described above.</b> It pins
/// the decision and carries its reasoning, so making the index unique fails here first rather than in
/// production at team creation.
/// </para>
/// </remarks>
public class InviteKeyIndexTests
{
    private const string IndexName = "TeamMemberInviteKey";
    private const string IndexedField = "Members.Invitation.InviteKey";
    private const string KnownOtherIndex = "UniqueTeamMemberKey";

    private static string CollectionSource()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !dir.GetDirectories("Tharga.Team.MongoDB").Any()) dir = dir.Parent;

        var path = Path.Combine(dir!.FullName, "Tharga.Team.MongoDB", "TeamRepositoryCollection.cs");

        Assert.True(File.Exists(path), $"Could not find the index definitions at '{path}'.");

        return File.ReadAllText(path);
    }

    /// <summary>
    /// The scan's own check. A scan that has stopped matching anything passes forever while reading as
    /// "everything verified", so it has to find an index it is not itself about.
    /// </summary>
    [Fact]
    public void TheScan_FindsTheIndexDefinitions()
    {
        Assert.Contains(KnownOtherIndex, CollectionSource());
    }

    [Fact]
    public void TheInviteKeyIndex_ExistsAndCoversTheInvitationCode()
    {
        var source = CollectionSource();

        Assert.Contains(IndexName, source);
        Assert.Contains(IndexedField, source);
    }

    /// <summary>See the remarks on this class. Making it unique breaks saving the second team.</summary>
    [Fact]
    public void TheInviteKeyIndex_IsNotUnique()
    {
        var source = CollectionSource();

        // From the indexed path to the end of that index entry, so only its own options are read.
        var match = Regex.Match(source, Regex.Escape(IndexedField) + @"(.*?)\)\s*\]", RegexOptions.Singleline);

        Assert.True(match.Success, $"Could not read the options of the '{IndexName}' index.");
        Assert.DoesNotContain("Unique = true", match.Groups[1].Value);
    }
}
