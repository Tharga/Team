using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// The assemblies the toolkit ships, read from the solution rather than listed by hand.
/// </summary>
/// <remarks>
/// <b>A guard that skips a package says nothing about it, and looks the same as one that checked it.</b> A
/// hand-written list goes stale the day a package is added. Every project in <c>Tharga.Team.sln</c> that is not
/// marked <c>IsPackable=false</c> ships, so that is the list. Each one has to load here; one that does not is
/// not yet referenced by this test project.
/// </remarks>
internal static partial class ShippedAssemblies
{
    private const string SolutionFile = "Tharga.Team.sln";

    [GeneratedRegex(@"^Project\(""\{[^}]+\}""\) = ""(?<name>[^""]+)"", ""(?<path>[^""]+\.csproj)""", RegexOptions.Multiline)]
    private static partial Regex ProjectLine();

    internal static DirectoryInfo RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, SolutionFile))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir;
    }

    /// <summary>The names of the shipped projects, which are also their assembly names.</summary>
    internal static string[] Names()
    {
        var root = RepositoryRoot().FullName;
        var solution = File.ReadAllText(Path.Combine(root, SolutionFile));

        return ProjectLine().Matches(solution)
            .Where(m => IsPackable(Path.Combine(root, m.Groups["path"].Value.Replace('\\', Path.DirectorySeparatorChar))))
            .Select(m => m.Groups["name"].Value)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();
    }

    internal static Assembly[] All() => Names().Select(Load).ToArray();

    private static Assembly Load(string name)
    {
        try
        {
            return Assembly.Load(new AssemblyName(name));
        }
        catch (FileNotFoundException e)
        {
            throw new InvalidOperationException($"'{name}' ships, but Tharga.Team.Blazor.Tests does not reference it, so no guard here can see it. Add a ProjectReference.", e);
        }
    }

    private static bool IsPackable(string projectPath)
        => !XDocument.Load(projectPath).Descendants("IsPackable")
            .Any(e => string.Equals(e.Value.Trim(), "false", StringComparison.OrdinalIgnoreCase));
}
