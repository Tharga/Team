using System.Text.RegularExpressions;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// Every invitation the components send goes through the overload that carries the team key (#289).
/// </summary>
/// <remarks>
/// A source scan, because the call sits inside a dialog flow no rendered test reaches. The four-string overload
/// still compiles, so reverting to it would silently strip the key from every host's sender.
/// </remarks>
public class InviteMailCallSiteTests
{
    private static readonly Regex Call = new(@"\.SendInviteAsync\(\s*(?<first>new\s+TeamInviteMail\b|[^\s,)]+)", RegexOptions.Compiled);

    private static DirectoryInfo SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Blazor"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return new DirectoryInfo(Path.Combine(dir.FullName, "Tharga.Team.Blazor"));
    }

    private static (string File, string FirstArgument)[] Calls()
    {
        var root = SourceRoot();

        return [.. root.GetFiles("*", SearchOption.AllDirectories)
            .Where(f => f.Extension is ".razor" or ".cs")
            .Where(f => !f.FullName.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !f.FullName.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .SelectMany(f => Call.Matches(File.ReadAllText(f.FullName))
                .Select(m => (Path.GetRelativePath(root.FullName, f.FullName), m.Groups["first"].Value)))];
    }

    [Fact]
    public void TheScanFindsTheInvitationCall()
    {
        Assert.Contains(Calls(), x => x.File.EndsWith("TeamComponent.razor", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryInvitationCall_PassesATeamInviteMail()
    {
        var offenders = Calls().Where(x => !Regex.IsMatch(x.FirstArgument, @"^new\s+TeamInviteMail$")).ToArray();

        Assert.Empty(offenders);
    }
}
