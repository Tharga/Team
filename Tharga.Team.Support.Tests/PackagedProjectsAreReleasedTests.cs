namespace Tharga.Team.Support.Tests;

/// <summary>
/// Every project that produces a package is packed by the release workflow.
/// </summary>
/// <remarks>
/// The pack list in <c>.github/workflows/build.yml</c> is one hand-written line per project. Adding a
/// project to the solution does not add it there, and nothing fails if it is missed — the build is
/// green, the release succeeds, and the package simply never reaches NuGet. That is the worst shape a
/// failure can take: it looks exactly like success.
/// <para>
/// This was nearly the outcome for <c>Tharga.Team.Support</c> itself. The support module is expected to
/// grow at least one more package (a Blazor half for the customer portal), so the omission would have
/// had a second chance to happen.
/// </para>
/// <para>
/// Lives here rather than in a shared test project because there is no shared one, and the package that
/// prompted the guard is as good a home as any. It scans the repository, not this project.
/// </para>
/// </remarks>
public class PackagedProjectsAreReleasedTests
{
    /// <summary>
    /// Projects that build a library but are deliberately not shipped. Listed explicitly so a new
    /// unpackaged project is a decision someone wrote down rather than a silent omission.
    /// </summary>
    /// <remarks>
    /// <c>Tharga.Team.Analyzers</c> is not a package of its own: <c>Tharga.Team</c> packs its dll under
    /// <c>analyzers/dotnet/cs</c>, so it reaches NuGet inside that package.
    /// </remarks>
    private static readonly string[] NotPackaged = ["Tharga.Team.Sample", "Tharga.Team.Analyzers"];

    private static DirectoryInfo RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Tharga.Team.sln"))) dir = dir.Parent;
        return dir;
    }

    private static string WorkflowPath()
        => Path.Combine(RepoRoot()!.FullName, ".github", "workflows", "build.yml");

    private static string[] PackableProjects()
    {
        var root = RepoRoot();
        Assert.NotNull(root);

        return root.GetDirectories("Tharga.Team*")
            .Select(d => d.Name)
            .Where(name => !name.EndsWith(".Tests", StringComparison.Ordinal))
            .Where(name => !NotPackaged.Contains(name))
            .Where(name => File.Exists(Path.Combine(root.FullName, name, $"{name}.csproj")))
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    /// <summary>
    /// The self-check. Three guards in this repo have shipped passing while examining nothing — a scan
    /// that found no files, a path built with the wrong separator, an assembly never loaded. A scan that
    /// cannot prove it found the repository is not evidence, so this fails rather than skips.
    /// </summary>
    [Fact]
    public void TheScanFindsTheRepositoryAndItsProjects()
    {
        var root = RepoRoot();
        Assert.True(root != null, "Could not locate the repository root (no Tharga.Team.sln above the test binary).");
        Assert.True(File.Exists(WorkflowPath()), $"Release workflow not found at {WorkflowPath()}.");

        var projects = PackableProjects();

        Assert.Contains("Tharga.Team", projects);
        Assert.Contains("Tharga.Team.Support", projects);
        Assert.True(projects.Length >= 7, $"Expected at least 7 packable projects, found {projects.Length}.");
    }

    [Fact]
    public void EveryPackableProject_IsPackedByTheReleaseWorkflow()
    {
        var workflow = File.ReadAllText(WorkflowPath());

        // Forward slashes: the workflow runs on Linux and the path in it is literal, so this must not be
        // built with Path.Combine. A guard that used the host separator would pass on Windows and report
        // every project missing on CI.
        var missing = PackableProjects()
            .Where(name => !workflow.Contains($"dotnet pack {name}/{name}.csproj", StringComparison.Ordinal))
            .ToArray();

        Assert.True(missing.Length == 0,
            $"Not packed by .github/workflows/build.yml, so they would never reach NuGet: {string.Join(", ", missing)}.");
    }

    /// <summary>
    /// The self-check for the guard above: a project the workflow does not name must be detected. Without
    /// this, "nothing missing" could equally mean the match never fires.
    /// </summary>
    [Fact]
    public void TheDetector_NoticesAProjectTheWorkflowDoesNotPack()
    {
        var workflow = File.ReadAllText(WorkflowPath());

        Assert.DoesNotContain("dotnet pack Tharga.Team.NoSuchPackage/Tharga.Team.NoSuchPackage.csproj", workflow);
        Assert.Contains("dotnet pack Tharga.Team.Support/Tharga.Team.Support.csproj", workflow);
    }
}
