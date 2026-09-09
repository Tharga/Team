namespace Tharga.Team.Blazor.Tests;

/// <summary>
/// No Razor comment sits inside a tag's attribute list.
/// </summary>
/// <remarks>
/// <b>Razor does not strip a comment in attribute position — it emits it as an attribute name.</b> The
/// browser rejects the <c>setAttribute</c>, the render batch fails to apply, and the circuit is terminated.
/// <para>
/// It shipped in <c>TeamComponent</c>'s "Your access" badge, so <c>/team</c> died for any viewer holding an
/// access level on a team — and took the **Create new Team** button with it, meaning a host that had
/// configured <c>CreateTeamPath</c> could never reach its own page (Tharga/Team#268).
/// </para>
/// <para>
/// <b>Nothing that runs could have caught it.</b> It compiles; it fails in a browser applying a render
/// batch. No unit test, no container check and no architecture test reaches that, which is why the guard has
/// to read the source.
/// </para>
/// </remarks>
public class RazorCommentPlacementTests
{
    [Fact]
    public void NoRazorCommentSitsInsideATag()
    {
        var offenders = new List<string>();

        foreach (var file in Directory.GetFiles(ComponentRoot(), "*.razor", SearchOption.AllDirectories))
        {
            foreach (var line in CommentsInsideTags(File.ReadAllText(file)))
            {
                offenders.Add($"{Path.GetRelativePath(ComponentRoot(), file)}:{line}");
            }
        }

        Assert.True(offenders.Count == 0,
            "A Razor comment inside a tag's attribute list is emitted as an attribute name, which the " +
            "browser rejects — the render batch fails and the circuit is terminated. Move the comment above " +
            $"the tag. Found at: {string.Join(", ", offenders)}");
    }

    /// <summary>
    /// A source scan that matches nothing passes forever while reading as "everything checked". These pin
    /// that it sees files, catches the shape it is looking for, and does not flag the correct form.
    /// </summary>
    [Fact]
    public void TheScan_ReadsSomething()
    {
        Assert.NotEmpty(Directory.GetFiles(ComponentRoot(), "*.razor", SearchOption.AllDirectories));
    }

    [Fact]
    public void TheScan_CatchesTheShapeThatShipped()
    {
        const string offending = """
            <RadzenBadge BadgeStyle="@(Enum.Parse<BadgeStyle>(Style(level)))"
                         Variant="Variant.Flat"
                         @* a comment where an attribute should be *@
                         Text="@Text(level)" />
            """;

        Assert.Equal([3], CommentsInsideTags(offending));
    }

    /// <summary>
    /// The generic argument matters: <c>Enum.Parse&lt;BadgeStyle&gt;</c> puts angle brackets inside an
    /// attribute value, so a scanner that treats every <c>&gt;</c> as the end of a tag walks straight past
    /// the very line that shipped.
    /// </summary>
    [Fact]
    public void TheScan_IsNotFooledByAngleBracketsInsideAttributeValues()
    {
        const string offending = """
            <Component Value="@(Enum.Parse<Kind>(x))" @* here *@ Other="1" />
            """;

        Assert.Equal([1], CommentsInsideTags(offending));
    }

    [Fact]
    public void TheScan_AcceptsACommentBetweenTags()
    {
        const string fine = """
            @* a comment where a comment belongs *@
            <RadzenBadge Text="@Text(level)" />
            """;

        Assert.Empty(CommentsInsideTags(fine));
    }

    /// <summary>A comment inside markup content, rather than inside a tag, is also fine.</summary>
    [Fact]
    public void TheScan_AcceptsACommentInsideAnElementsContent()
    {
        const string fine = """
            <RadzenStack>
                @* explaining the child below *@
                <RadzenBadge Text="x" />
            </RadzenStack>
            """;

        Assert.Empty(CommentsInsideTags(fine));
    }

    /// <summary>
    /// The 1-based lines carrying a <c>@*</c> that opens while inside a tag.
    /// </summary>
    /// <remarks>
    /// Written as a scan over characters rather than a regular expression because the thing being matched
    /// spans lines and contains both quotes and angle brackets — a pattern for it is either wrong or
    /// unreadable, and the first attempt at one silently reported the shipped defect as clean.
    /// </remarks>
    private static IReadOnlyList<int> CommentsInsideTags(string source)
    {
        var found = new List<int>();
        var insideTag = false;
        var quote = '\0';
        var parens = 0;

        for (var i = 0; i < source.Length; i++)
        {
            var c = source[i];

            if (!insideTag)
            {
                if (c == '<' && i + 1 < source.Length && char.IsLetter(source[i + 1])) insideTag = true;
                continue;
            }

            if (quote != '\0')
            {
                if (c == quote) quote = '\0';
                continue;
            }

            switch (c)
            {
                case '"' or '\'':
                    quote = c;
                    break;
                case '(':
                    parens++;
                    break;
                case ')':
                    parens--;
                    break;
                case '>' when parens <= 0:
                    insideTag = false;
                    break;
                case '@' when i + 1 < source.Length && source[i + 1] == '*':
                    found.Add(source.Take(i).Count(x => x == '\n') + 1);
                    i++;
                    break;
            }
        }

        return found;
    }

    private static string ComponentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Tharga.Team.Blazor")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(dir.FullName, "Tharga.Team.Blazor");
    }
}
