using System.Text.RegularExpressions;
using Tharga.Team.Blazor.Framework;

namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// HTML is rendered as markup in one place only, <see cref="RichTextView"/>, and only as the sanitizer's output.
/// </summary>
/// <remarks>
/// <b>A guard, because the hole is one line anywhere.</b> <c>(MarkupString)message.BodyHtml</c> compiles, renders
/// beautifully and runs whatever a sender put in their mail in the reader's session. Nothing that runs would notice;
/// only reading the source does.
/// </remarks>
public partial class RawHtmlRenderingTests
{
    private const string TheView = "Framework/RichTextView.razor";

    [Fact]
    public void MarkupIsRendered_OnlyByTheView()
    {
        var offenders = Sources()
            .Where(x => MarkupRendering().IsMatch(x.Text))
            .Select(x => x.Path)
            .Where(x => x != TheView)
            .ToArray();

        Assert.True(offenders.Length == 0,
            "Only RichTextView may render HTML as markup, and only through SafeHtml. Render formatted text with " +
            $"<RichTextView Html=\"...\" /> instead. Found in: {string.Join(", ", offenders)}");
    }

    [Fact]
    public void TheView_RendersOnlyWhatTheSanitizerReturned()
    {
        var view = Sources().Single(x => x.Path == TheView).Text;

        var constructions = MarkupRendering().Matches(view).Select(x => view[x.Index..view.IndexOf(';', x.Index)]).ToArray();

        Assert.NotEmpty(constructions);
        Assert.All(constructions, x => Assert.Contains("SafeHtml.Sanitize(", x));
    }

    /// <summary>A source scan that matches nothing passes forever; these pin that it reads files and sees the shape.</summary>
    [Fact]
    public void TheScan_ReadsTheLibrary_AndRecognisesMarkupRendering()
    {
        Assert.Contains(Sources(), x => x.Path == TheView);
        Assert.Contains(Sources(), x => x.Path == "Features/Support/SupportTranscript.razor");

        Assert.Matches(MarkupRendering(), "@((MarkupString)message.BodyHtml)");
        Assert.Matches(MarkupRendering(), "_markup = new MarkupString(html);");
        Assert.Matches(MarkupRendering(), "builder.AddMarkupContent(0, html);");
        Assert.DoesNotMatch(MarkupRendering(), "private MarkupString _markup;");
    }

    private static IEnumerable<(string Path, string Text)> Sources()
    {
        var root = ComponentRoot();

        return Directory.GetFiles(root, "*.*", SearchOption.AllDirectories)
            .Where(x => x.EndsWith(".razor", StringComparison.Ordinal) || x.EndsWith(".cs", StringComparison.Ordinal))
            .Where(x => !x.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") && !x.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            .Select(x => (Path.GetRelativePath(root, x).Replace(Path.DirectorySeparatorChar, '/'), File.ReadAllText(x)));
    }

    private static string ComponentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Blazor")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "Tharga.Team.Blazor");
    }

    [GeneratedRegex(@"\(MarkupString\)|new MarkupString\(|AddMarkupContent\(")]
    private static partial Regex MarkupRendering();
}
