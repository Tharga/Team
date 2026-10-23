namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// No source file holds a raw NUL byte.
/// </summary>
/// <remarks>
/// <b>The failure is silent, which is why it is tested.</b> The compiler accepts a NUL inside a string literal, but
/// git then treats the whole file as binary: no diff, no blame, and ripgrep skips it, so a search for the class
/// that file declares comes back empty. <c>TeamClaimRevalidator</c> went that way through a literal separator.
/// Write <c>"\0"</c> instead.
/// </remarks>
public class SourceHygieneTests
{
    private static readonly string[] Extensions = [".cs", ".razor", ".csproj", ".props", ".targets", ".md", ".json", ".yml", ".yaml", ".xml"];
    private static readonly string[] SkippedDirectories = ["bin", "obj", ".git", ".vs", "node_modules", "_site"];

    private static DirectoryInfo RepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Tharga.Team.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir;
    }

    private static IEnumerable<string> SourceFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory))
        {
            if (Extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) yield return file;
        }

        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            if (SkippedDirectories.Contains(Path.GetFileName(child), StringComparer.OrdinalIgnoreCase)) continue;
            foreach (var file in SourceFiles(child)) yield return file;
        }
    }

    private static bool HoldsNul(string path) => File.ReadAllBytes(path).Contains((byte)0);

    [Fact]
    public void NoSourceFile_HoldsARawNulByte()
    {
        var root = RepositoryRoot();

        var offenders = SourceFiles(root.FullName)
            .Where(HoldsNul)
            .Select(x => Path.GetRelativePath(root.FullName, x).Replace(Path.DirectorySeparatorChar, '/'))
            .ToArray();

        Assert.True(
            offenders.Length == 0,
            $"These files hold a raw NUL byte, so git treats them as binary and searches skip them: {string.Join(", ", offenders)}. " +
            "Replace it with the escape \\0 in the literal.");
    }

    [Fact]
    public void TheGuard_ActuallyReadsTheSource_AndSeesANul()
    {
        var root = RepositoryRoot();
        Assert.True(SourceFiles(root.FullName).Count() > 200, "The scan is not reaching the repository's source.");

        var file = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.cs");
        try
        {
            File.WriteAllBytes(file, [(byte)'"', 0, (byte)'"']);
            Assert.True(HoldsNul(file));

            File.WriteAllText(file, "\"\\0\"");
            Assert.False(HoldsNul(file));
        }
        finally
        {
            File.Delete(file);
        }
    }
}
