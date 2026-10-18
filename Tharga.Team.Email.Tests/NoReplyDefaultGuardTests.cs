using System.Text.RegularExpressions;

namespace Tharga.Team.Email.Tests;

/// <summary>No default the toolkit ships names a no-reply address: not a string in its code, not a sample's settings, not a code example in its documentation.</summary>
/// <remarks>
/// Every mail the toolkit sends should come from an address a reply reaches, because a no-reply address loses every
/// reply and teaches recipients not to answer. A default is what a host copies, so the scan covers what can be copied:
/// string literals in shipped code, the samples' settings files, and the code blocks of the READMEs and articles.
/// Comments and prose may still name the practice in order to warn against it. Tests are not scanned.
/// </remarks>
public class NoReplyDefaultGuardTests
{
    private static readonly Regex NoReply = new(@"no-?reply", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex Literal = new(
        "\"\"\"[\\s\\S]*?\"\"\"|@\"(?:[^\"]|\"\")*\"|\"(?:[^\"\\\\\\r\\n]|\\\\.)*\"",
        RegexOptions.Compiled);

    private static readonly Regex CommentLine = new(@"^\s*(//|/\*|\*|@\*).*$", RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex CodeBlock = new(@"^```[^\n]*\n(?<code>[\s\S]*?)^```", RegexOptions.Multiline | RegexOptions.Compiled);

    private static DirectoryInfo Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Tharga.Team.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        return dir;
    }

    private static bool Shipped(FileInfo file, DirectoryInfo root)
    {
        var parts = Path.GetRelativePath(root.FullName, file.FullName).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return !parts.Any(x => x is "bin" or "obj" or "_site" or "api" or "node_modules" or "plan" or ".git" or ".claude")
               && !parts[0].EndsWith(".Tests", StringComparison.Ordinal);
    }

    private static FileInfo[] Files(string pattern)
    {
        var root = Root();

        return [.. root.GetFiles(pattern, SearchOption.AllDirectories).Where(x => Shipped(x, root))];
    }

    /// <summary>The string literals of a C# or Razor source, without the lines that are comments.</summary>
    internal static IEnumerable<string> LiteralsOf(string source)
        => Literal.Matches(CommentLine.Replace(source, string.Empty)).Select(x => x.Value);

    /// <summary>The code blocks of a Markdown document.</summary>
    internal static IEnumerable<string> CodeBlocksOf(string markdown)
        => CodeBlock.Matches(markdown.Replace("\r\n", "\n")).Select(x => x.Groups["code"].Value);

    private static string[] Offenders(IEnumerable<FileInfo> files, Func<string, IEnumerable<string>> parts)
        => [.. files.SelectMany(file => parts(File.ReadAllText(file.FullName)).Where(x => NoReply.IsMatch(x)).Select(x => $"{Path.GetRelativePath(Root().FullName, file.FullName)}: {x.Trim()}"))];

    [Fact]
    public void NoStringInTheShippedCode_NamesANoReplyAddress()
    {
        var files = Files("*.cs").Concat(Files("*.razor")).ToArray();

        Assert.True(files.Length > 100, $"Only {files.Length} source files were found, so the scan is not looking where the code is.");
        Assert.Empty(Offenders(files, LiteralsOf));
    }

    [Fact]
    public void NoSamplesSettings_NameANoReplyAddress()
    {
        var files = Files("appsettings*.json").Concat(Files("launchSettings.json")).ToArray();

        Assert.NotEmpty(files);
        Assert.Empty(Offenders(files, content => [content]));
    }

    [Fact]
    public void NoCodeExampleInTheDocumentation_NamesANoReplyAddress()
    {
        var files = Files("*.md");

        Assert.Contains(files, x => x.Name == "user-mail.md" || x.Name == "team-email.md");
        Assert.Empty(Offenders(files, CodeBlocksOf));
    }

    [Theory]
    [InlineData("var from = \"noreply@example.com\";")]
    [InlineData("public string FromAddress { get; set; } = \"No-Reply@example.com\";")]
    [InlineData("var body = $\"\"\"\n    Sent from no-reply@example.com\n    \"\"\";")]
    [InlineData("var from = @\"noreply@example.com\";")]
    public void TheScan_FindsANoReplyAddressInALiteral(string source)
    {
        Assert.Contains(LiteralsOf(source), x => NoReply.IsMatch(x));
    }

    [Theory]
    [InlineData("// An invitation used to come from \"noreply@\".")]
    [InlineData("/// <summary>Never a \"no-reply\" address.</summary>")]
    [InlineData("    * a no-reply address loses every reply")]
    public void TheScan_LeavesCommentsAlone(string source)
    {
        Assert.DoesNotContain(LiteralsOf(source), x => NoReply.IsMatch(x));
    }

    [Fact]
    public void TheScan_FindsANoReplyAddressInACodeBlock_AndNotInProse()
    {
        const string markdown = "A no-reply address loses replies.\n\n```csharp\no.FromAddress = \"noreply@example.com\";\n```\n";

        Assert.Single(CodeBlocksOf(markdown), x => NoReply.IsMatch(x));
        Assert.DoesNotContain(CodeBlocksOf("A no-reply address loses replies.\n"), x => NoReply.IsMatch(x));
    }
}
